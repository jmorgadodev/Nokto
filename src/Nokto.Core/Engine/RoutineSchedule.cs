using System.Globalization;
using Nokto.Core.Models;

namespace Nokto.Core.Engine;

/// <summary>Local wall-clock schedule; no network clock, persisted countdown or busy waiting.</summary>
public static class RoutineSchedule
{
    public static DateTimeOffset NextOccurrence(TriggerDefinition trigger, DateTimeOffset now, TimeZoneInfo zone, DateOnly? lastFiredDate = null)
    {
        var parameters = trigger.Parameters;
        string? timeText = parameters?.TryGetValue("timeOfDay", out var time) == true ? time.GetString() : null;
        if (!TimeSpan.TryParse(timeText, CultureInfo.InvariantCulture, out var at) || at < TimeSpan.Zero || at >= TimeSpan.FromDays(1))
            throw new InvalidOperationException("Indica una hora válida entre 00:00 y 23:59.");
        var days = parameters?.TryGetValue("daysOfWeek", out var value) == true
            ? value.EnumerateArray().Select(d => d.GetInt32()).ToHashSet() : Enumerable.Range(0, 7).ToHashSet();
        if (days.Count == 0 || days.Any(d => d is < 0 or > 6))
            throw new InvalidOperationException("Selecciona al menos un día válido de la semana.");
        var local = TimeZoneInfo.ConvertTime(now, zone);
        for (int offset = 0; offset <= 7; offset++)
        {
            var date = local.Date.AddDays(offset);
            if (!days.Contains((int)date.DayOfWeek) || DateOnly.FromDateTime(date) == lastFiredDate) continue;
            var candidate = DateTime.SpecifyKind(date + at, DateTimeKind.Unspecified);
            // A skipped DST time fires at the next valid local minute.
            while (zone.IsInvalidTime(candidate)) candidate = candidate.AddMinutes(1);
            var offsets = zone.IsAmbiguousTime(candidate) ? zone.GetAmbiguousTimeOffsets(candidate) : [zone.GetUtcOffset(candidate)];
            foreach (var instant in offsets.Select(o => new DateTimeOffset(candidate, o)).OrderBy(d => d.UtcDateTime))
                if (instant > now) return instant;
        }
        throw new InvalidOperationException("No se pudo calcular el próximo horario.");
    }
}
