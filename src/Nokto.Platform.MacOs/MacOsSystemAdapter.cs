using Nokto.Core.Abstractions;
using Nokto.Core.Models;

namespace Nokto.Platform.MacOs;

/// <summary>
/// Adaptador de sistema para macOS (preparado para Fase 5 / multiplataforma futura mediante IOKit / pmset).
/// </summary>
public sealed class MacOsSystemAdapter : ISystemAdapter
{
    public Task SetPowerStateAsync(PowerAction action, bool force = false, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("El soporte nativo para macOS se implementará en fases posteriores.");
    }

    public Task SetDisplayPowerAsync(bool turnOn, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("El soporte nativo para macOS se implementará en fases posteriores.");
    }

    public Task SetMasterVolumeFadeAsync(float targetVolume, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("El soporte nativo para macOS se implementará en fases posteriores.");
    }

    public Task<float> GetMasterVolumeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(1.0f);
    }

    public Task SetMasterVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> GetMuteAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public void SimulateKeepAlivePulse(KeepAliveMode mode)
    {
    }

    public Task RunKeepAliveLoopAsync(
        KeepAliveMode mode,
        int jitterMinSeconds,
        int jitterMaxSeconds,
        Action<string>? onActivityLogged,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public SystemMetrics GetCurrentMetrics()
    {
        return new SystemMetrics
        {
            CpuUsagePercentage = 0,
            RamUsedMb = 0,
            RamTotalMb = 0,
            NetworkDownKBs = 0,
            NetworkUpKBs = 0,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public Task<byte[]> CaptureScreenAsync(bool stampMetadata = false, string? label = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Array.Empty<byte>());
    }

    public void Dispose()
    {
    }
}
