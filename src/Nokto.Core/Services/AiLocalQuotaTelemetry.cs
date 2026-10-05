using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nokto.Core.Services;

internal sealed class AiLocalQuotaTelemetry(string defaultModel)
{
    private sealed record Sample(AiQuotaMetric Metric, DateTimeOffset ObservedAt, DateTimeOffset? Reset);
    private readonly Dictionary<(string Model, string Window), Sample> _samples = [];
    private readonly Dictionary<string, (double Value, DateTimeOffset ObservedAt)> _counters = [];
    private static readonly Regex Markers = new("gemini|claude|quota|credits|remaining|window|tokens", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TextQuota = new(
        @"(?<model>Gemini|Claude|GPT)[^\r\n{}]{0,80}?(?<window>5\s*h|five.hour|weekly|semanal)[^\r\n{}]{0,40}?(?<kind>remaining|restante|used|consumido)\s*[:=]?\s*(?<percent>\d{1,3}(?:\.\d+)?)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public bool HasExpiredQuota { get; private set; }
    public List<AiQuotaMetric> Metrics => _samples.Values.Select(s => s.Metric).ToList();
    public double? FiveHourUsage => Usage("Ventana 5h");
    public double? WeeklyUsage => Usage("Semanal");
    public DateTimeOffset? Reset => _samples.Values.Select(s => s.Reset).Where(s => s.HasValue).Min();

    private double? Usage(string window)
    {
        var values = _samples.Where(s => s.Key.Window == window).Select(s => 100 - s.Value.Metric.RemainingPercent).ToList();
        return values.Count == 0 ? null : values.Max();
    }

    public string StatusNote
    {
        get
        {
            var parts = new List<string>();
            if (_samples.Count > 0) parts.Add("Cuotas extraídas de telemetría local");
            foreach (var counter in _counters) parts.Add($"{counter.Key}: {counter.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)}");
            if (HasExpiredQuota) parts.Add("Cuota almacenada vencida");
            if (_samples.Count == 0) parts.Add("[ Sesión Activa ] - Cuota en memoria / cliente");
            return string.Join("; ", parts);
        }
    }

    public void SetCounter(string label, double value, DateTimeOffset observedAt)
    {
        if (!double.IsFinite(value) || value < 0) return;
        if (!_counters.TryGetValue(label, out var previous) || previous.ObservedAt <= observedAt)
            _counters[label] = (value, observedAt);
    }

    public void Add(string model, string window, double remaining, DateTimeOffset observedAt, DateTimeOffset? reset = null, string? detail = null)
    {
        if (!double.IsFinite(remaining) || remaining is < 0 or > 100) return;
        if (reset <= DateTimeOffset.UtcNow) { HasExpiredQuota = true; return; }
        var key = (model, window);
        if (!_samples.ContainsKey(key) && _samples.Count >= 64) return;
        if (_samples.TryGetValue(key, out var existing) && existing.ObservedAt > observedAt) return;
        string label = model == defaultModel ? window : $"{model} · {window}";
        _samples[key] = new Sample(new AiQuotaMetric(model, label, remaining, detail ?? $"{remaining:0.#}% restante"), observedAt, reset);
    }

    public bool ReadJson(string json, DateTimeOffset observedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            Visit(document.RootElement, defaultModel, null, observedAt, 0);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        return false;
    }

    private void Visit(JsonElement value, string model, string? window, DateTimeOffset observedAt, int depth, DateTimeOffset? inheritedReset = null)
    {
        if (depth > 16) return;
        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (text?.TrimStart().StartsWith('{') == true || text?.TrimStart().StartsWith('[') == true)
            {
                try
                {
                    using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 });
                    Visit(document.RootElement, model, window, observedAt, depth + 1, inheritedReset);
                }
                catch (JsonException) { }
            }
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray()) Visit(child, model, window, observedAt, depth + 1, inheritedReset);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            string key = Normalize(property.Name);
            if (key is "model" or "modelname" or "label" or "displayname" or "provider")
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 and <= 100 } name)
                {
                    if (defaultModel == "OpenCode" || IsModel(name)) model = name;
                }
            }
            if (key is "window" or "windowtype" && property.Value.ValueKind == JsonValueKind.String)
                window = WindowName(property.Value.GetString()) ?? window;
            if (key is "windowminutes" or "windowdurationmins" && Number(property.Value, out double minutes))
                window = minutes == 300 ? "Ventana 5h" : minutes == 10080 ? "Semanal" : "Cuota";
            if (key == "timestamp" && property.Value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(property.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
                observedAt = timestamp;
        }
        DateTimeOffset? reset = inheritedReset;
        foreach (var property in value.EnumerateObject())
            if (Normalize(property.Name) is "resetsat" or "resettime") reset = ResetTime(property.Value);

        ReadTokenLimit(value, model, observedAt, reset);
        foreach (var property in value.EnumerateObject())
        {
            string key = Normalize(property.Name);
            string? metricWindow = key switch
            {
                "windowusagepercent" or "fivehourusagepercent" or "fivehourusagepercentage" or "fivehourremainingpercent" => "Ventana 5h",
                "weeklyusagepercent" or "weeklyusagepercentage" or "weeklyremainingpercent" => "Semanal",
                "remainingpercent" or "remainingpercentage" or "usedpercent" or "usagepercent" or "remainingfraction" => window ?? "Cuota",
                "quotapercent" when defaultModel == "OpenCode" => "Sesión Local",
                _ => null
            };
            if (metricWindow != null && Number(property.Value, out double number))
            {
                double remaining = key == "remainingfraction" ? number * 100 :
                    key.Contains("remaining", StringComparison.Ordinal) || key == "quotapercent" ? number : 100 - number;
                Add(model, metricWindow, remaining, observedAt, reset);
            }
            if (key is "tokensconsumed" or "totaltokensconsumed")
                if (Number(property.Value, out double count)) SetCounter("Tokens consumidos", count, observedAt);
            if (key is "availablecredits" or "creditbalance")
                if (Number(property.Value, out double credits)) SetCounter("Créditos IA disponibles", credits, observedAt);
            // Credentials, conversation content and configuration examples are never telemetry.
            if (key is "content" or "messages" or "message" or "prompt" or "output" or "examples" or "apikey" or "token" or "credentials") continue;
            string childModel = IsModel(property.Name) ? property.Name : model;
            string? childWindow = WindowName(property.Name) ?? window;
            Visit(property.Value, childModel, childWindow, observedAt, depth + 1, reset);
        }
    }

    private void ReadTokenLimit(JsonElement value, string model, DateTimeOffset observedAt, DateTimeOffset? reset)
    {
        if (TryNumber(value, ["tokensConsumed", "totalTokensConsumed", "tokensUsed"], out double tokens) && tokens >= 0)
        {
            SetCounter("Tokens consumidos", tokens, observedAt);
            if (TryNumber(value, ["tokenLimit", "tokensLimit", "totalTokenLimit"], out double cap) && cap > 0)
                Add(model, "Tokens", Math.Clamp(100 * (1 - tokens / cap), 0, 100), observedAt, reset);
        }
        if (!value.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        bool hasLimits = value.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Object;
        foreach (var (key, window) in new[] { ("totalTokens", "Tokens"), ("weeklyTokens", "Semanal"), ("fiveHourTokens", "Ventana 5h") })
        {
            if (!usage.TryGetProperty(key, out var consumed) || !Number(consumed, out double used) || used < 0) continue;
            SetCounter("Tokens consumidos", used, observedAt);
            if (hasLimits && limits.TryGetProperty(key, out var ceiling) && Number(ceiling, out double cap) && cap > 0)
                Add(model, window, Math.Clamp(100 * (1 - used / cap), 0, 100), observedAt, reset);
        }
    }

    private static bool TryNumber(JsonElement value, string[] names, out double number)
    {
        foreach (string name in names)
            if (value.TryGetProperty(name, out var property) && Number(property, out number)) return true;
        number = 0;
        return false;
    }

    public void ReadText(string text, DateTimeOffset observedAt)
    {
        try
        {
            if (!Markers.IsMatch(text)) return;
            if (ReadJson(text, observedAt)) return;
            foreach (string line in text.Split('\n'))
            {
                if (!Markers.IsMatch(line)) continue;
                int start = -1, depth = 0;
                bool quoted = false, escaped = false;
                bool hadJson = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                    if (c == '"' && depth > 0) { quoted = true; continue; }
                    if (c == '{') { if (depth++ == 0) start = i; }
                    if (c == '}' && depth > 0 && --depth == 0 && start >= 0)
                        hadJson |= ReadJson(line[start..(i + 1)], observedAt);
                }
                if (hadJson) continue;
                foreach (Match match in TextQuota.Matches(line))
                {
                    if (!double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)) continue;
                    string kind = match.Groups["kind"].Value;
                    Add(match.Groups["model"].Value, WindowName(match.Groups["window"].Value) ?? "Cuota",
                        kind.Equals("used", StringComparison.OrdinalIgnoreCase) || kind.Equals("consumido", StringComparison.OrdinalIgnoreCase) ? 100 - percent : percent, observedAt);
                }
            }
        }
        catch (RegexMatchTimeoutException) { }
    }

    private static bool IsModel(string name) => name.Contains("gemini", StringComparison.OrdinalIgnoreCase) || name.Contains("claude", StringComparison.OrdinalIgnoreCase) || name.Contains("gpt", StringComparison.OrdinalIgnoreCase);
    private static string? WindowName(string? name) => Normalize(name ?? "") switch
    {
        "5h" or "5hours" or "fivehour" or "fivehours" or "ventana5h" => "Ventana 5h",
        "weekly" or "week" or "semanal" or "7d" or "7days" => "Semanal",
        _ => null
    };
    private static string Normalize(string name) => name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
    private static bool Number(JsonElement value, out double number)
    {
        number = 0;
        bool parsed = value.ValueKind == JsonValueKind.Number ? value.TryGetDouble(out number) :
            value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        return parsed && double.IsFinite(number);
    }
    private static DateTimeOffset? ResetTime(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("seconds", out var seconds)) value = seconds;
        try
        {
            if (Number(value, out double unix)) return DateTimeOffset.FromUnixTimeSeconds(checked((long)unix));
            if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)) return timestamp;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException) { }
        return null;
    }
}
