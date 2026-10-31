using Windows.System.Power;

namespace Nokto.Platform.Windows.Notifications;

public sealed partial class IslandSystemObserver
{
    private readonly IslandBatteryMilestones _batteryMilestones = new();
    private EnergySaverStatus _energySaver;
    private void StartPowerNotices()
    {
        if (Wants("power")) try { _ac = HasAc(); PowerManager.PowerSupplyStatusChanged += PowerChanged; } catch (Exception) { }
        if (Wants("battery")) try
        {
            ReadBattery(); PowerManager.RemainingChargePercentChanged += BatteryChanged; PowerManager.BatteryStatusChanged += BatteryChanged;
        }
        catch (Exception) { }
        if (Wants("energySaver")) try { _energySaver = PowerManager.EnergySaverStatus; PowerManager.EnergySaverStatusChanged += EnergySaverChanged; } catch (Exception) { }
    }
    private void BatteryChanged(object? sender, object args) { try { ReadBattery(); } catch (Exception) { } }
    private void ReadBattery()
    {
        if (_disposed || PowerManager.BatteryStatus == BatteryStatus.NotPresent) return;
        IslandSystemNotice? notice;
        lock (_lifetimeLock) notice = _batteryMilestones.Observe(PowerManager.RemainingChargePercent, HasAc());
        if (notice is not null) Emit(notice);
    }
    private void EnergySaverChanged(object? sender, object args)
    {
        try
        {
            var status = PowerManager.EnergySaverStatus;
            if (status == _energySaver) return; _energySaver = status;
            Emit(new("energySaver", status == EnergySaverStatus.On ? "Ahorro de batería activado" : "Ahorro de batería desactivado", "Estado de energía de Windows"));
        }
        catch (Exception) { }
    }
    private void StopPowerNotices()
    {
        try { PowerManager.RemainingChargePercentChanged -= BatteryChanged; PowerManager.BatteryStatusChanged -= BatteryChanged; PowerManager.EnergySaverStatusChanged -= EnergySaverChanged; } catch (Exception) { }
    }
}

/// <summary>Umbrales por cambio, sin avisos de arranque ni repetición por cada porcentaje.</summary>
public sealed class IslandBatteryMilestones
{
    private int? _previous;
    private readonly HashSet<int> _reported = [];
    private bool _fullReported;
    public IslandSystemNotice? Observe(int percentage, bool charging)
    {
        if (percentage is < 0 or > 100) return null;
        int? before = _previous; _previous = percentage;
        if (percentage < 97) _fullReported = false;
        foreach (int threshold in new[] { 10, 20 }) if (percentage > threshold + 3) _reported.Remove(threshold);
        if (before is null) return null;
        if (charging && percentage == 100 && before < 100 && !_fullReported)
        { _fullReported = true; return new("battery", "Batería cargada", "Puedes desconectar el cargador", percentage); }
        if (charging) return null;
        foreach (int threshold in new[] { 10, 20 })
            if (percentage <= threshold && before > threshold && _reported.Add(threshold))
                return new("battery", threshold == 10 ? "Batería muy baja" : "Batería baja", "Conecta el cargador", percentage);
        return null;
    }
}
