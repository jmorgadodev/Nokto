using System.Globalization;
using System.Text.Json;

namespace Nokto.Core.Models;

public sealed record CodexQuotaWindow(double RemainingPercent, int? WindowMinutes, DateTimeOffset? ResetsAt)
{
    public bool Exhausted => RemainingPercent <= 0;
    public string WindowLabel => WindowMinutes == 10080 ? "Semana" : WindowMinutes is > 0 and var minutes
        ? minutes % 60 == 0 ? $"Ventana de {minutes / 60} h" : $"Ventana de {minutes} min" : "Ventana corta";
    public string ResetLabel(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (ResetsAt is not { } reset) return "Hora de restablecimiento no disponible";
        if (reset <= now) return "Esperando actualización de la cuota";
        var local = TimeZoneInfo.ConvertTime(reset, zone); var today = TimeZoneInfo.ConvertTime(now, zone);
        string date = local.Date == today.Date ? "hoy" : local.Date == today.Date.AddDays(1) ? "mañana" : local.ToString("ddd dd/MM", CultureInfo.GetCultureInfo("es-CL"));
        return $"{(Exhausted ? "Vuelve" : "Se restablece")} {date}, {local:HH:mm}";
    }
}

public sealed record CodexUsageSnapshot(CodexQuotaWindow? Primary, CodexQuotaWindow? Weekly, DateTimeOffset UpdatedAt)
{
    public bool Available => Primary is not null || Weekly is not null;
    public static CodexUsageSnapshot Parse(JsonElement result, DateTimeOffset now)
    {
        if (result.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Respuesta de cuota no válida.");
        JsonElement bucket = default;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object &&
            buckets.TryGetProperty("codex", out var codex) && codex.ValueKind == JsonValueKind.Object) bucket = codex;
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object) bucket = legacy;
        if (bucket.ValueKind != JsonValueKind.Object) return new(null, null, now);
        return new(ReadWindow(bucket, "primary"), ReadWindow(bucket, "secondary"), now);
    }
    private static CodexQuotaWindow? ReadWindow(JsonElement bucket, string name)
    {
        if (!bucket.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var usage) || usage.ValueKind != JsonValueKind.Number ||
            !usage.TryGetDouble(out double used) || !double.IsFinite(used)) return null;
        int? minutes = window.TryGetProperty("windowDurationMins", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetInt32(out int length) && length > 0 ? length : null;
        DateTimeOffset? reset = null;
        if (window.TryGetProperty("resetsAt", out var timestamp) && timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out long seconds))
            try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { }
        return new(Math.Clamp(100 - used, 0, 100), minutes, reset);
    }
}

public interface ICodexUsageReader
{
    Task<CodexUsageSnapshot> ReadAsync(CancellationToken cancellation);
}
