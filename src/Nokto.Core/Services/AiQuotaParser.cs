using System.Globalization;
using System.Text.Json;

namespace Nokto.Core.Services;

// Only explicitly named percentages and identified time windows are accepted.
// JsonDocument keeps this parser independent of reflection and serializer metadata.
internal sealed class AiQuotaParser
{
    internal sealed record Window(double UsedPercent, DateTimeOffset? ResetsAt, DateTimeOffset ObservedAt);
    public Window? FiveHour { get; private set; }
    public Window? Weekly { get; private set; }

    public void Read(string json, DateTimeOffset observedAt, bool codex, bool sessionEvent = false)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            if (sessionEvent)
            {
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var type) || type.GetString() != "event_msg" ||
                    !root.TryGetProperty("payload", out var payload) ||
                    !payload.TryGetProperty("type", out var payloadType) || payloadType.GetString() != "token_count" ||
                    !payload.TryGetProperty("rate_limits", out root)) return;
                if (document.RootElement.TryGetProperty("timestamp", out var timestamp) &&
                    DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
                    observedAt = instant;
            }
            Visit(root, observedAt, codex, null, 0);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            System.Diagnostics.Debug.WriteLine("Nokto: quota JSON unavailable or malformed.");
        }
    }

    private void Visit(JsonElement element, DateTimeOffset observedAt, bool codex, int? window, int depth)
    {
        if (depth > 16) return;
        if (element.ValueKind == JsonValueKind.String)
        {
            string? nested = element.GetString();
            if (nested?.TrimStart() is { } text && (text.StartsWith('{') || text.StartsWith('[')))
            {
                try
                {
                    using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 24 });
                    Visit(document.RootElement, observedAt, codex, window, depth + 1);
                }
                catch (JsonException) { }
            }
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) Visit(child, observedAt, codex, window, depth + 1);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            if (Normalize(property.Name) == "limitid" && codex &&
                property.Value.ValueKind == JsonValueKind.String &&
                !string.Equals(property.Value.GetString(), "codex", StringComparison.OrdinalIgnoreCase)) return;
        }

        DateTimeOffset? resetsAt = null;
        foreach (var property in element.EnumerateObject())
        {
            string key = Normalize(property.Name);
            if (key is "windowminutes" or "windowdurationmins" && Number(property.Value, out double minutes))
                window = minutes == 300 ? 300 : minutes == 10080 ? 10080 : -1;
            if (key == "resetsat") resetsAt = ResetTime(property.Value);
        }
        foreach (var property in element.EnumerateObject())
        {
            string key = Normalize(property.Name);
            int? targetWindow = key switch
            {
                "windowusagepercent" or "fivehourusagepercent" or "fivehourusagepercentage" or "fivehourremainingpercent" => 300,
                "weeklyusagepercent" or "weeklyusagepercentage" or "weeklyremainingpercent" => 10080,
                "usedpercent" or "usagepercent" or "remainingpercent" or "remainingpercentage" => window,
                _ => null
            };
            if (targetWindow is 300 or 10080 && Number(property.Value, out double percent) &&
                double.IsFinite(percent) && percent is >= 0 and <= 100)
            {
                double used = key.Contains("remaining", StringComparison.Ordinal) ? 100 - percent : percent;
                SetWindow(targetWindow.Value, new Window(used, resetsAt, observedAt));
            }
            // Never interpret arbitrary user text as telemetry.
            if (key is "message" or "messages" or "content" or "prompt" or "output") continue;
            int? childWindow = key switch
            {
                "fivehour" or "5h" => 300,
                "weekly" or "week" => 10080,
                _ => window
            };
            Visit(property.Value, observedAt, codex, childWindow, depth + 1);
        }
    }

    private void SetWindow(int minutes, Window value)
    {
        if (value.ResetsAt <= DateTimeOffset.UtcNow) return;
        var current = minutes == 300 ? FiveHour : Weekly;
        if (current != null && current.ObservedAt > value.ObservedAt) return;
        if (minutes == 300) FiveHour = value;
        else Weekly = value;
    }

    private static bool Number(JsonElement element, out double number)
    {
        number = 0;
        return element.ValueKind == JsonValueKind.Number ? element.TryGetDouble(out number) :
            element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static DateTimeOffset? ResetTime(JsonElement element)
    {
        try
        {
            if (Number(element, out double number) && double.IsFinite(number))
                return DateTimeOffset.FromUnixTimeSeconds(checked((long)number));
            if (element.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) return date;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException) { }
        return null;
    }

    private static string Normalize(string key) => key.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
}
