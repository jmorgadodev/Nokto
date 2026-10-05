using Nokto.Core.Abstractions;
using Nokto.Core.Models;

namespace Nokto.ConsoleTest;

/// <summary>Deterministic adapter: concurrency tests never affect actual power, input or audio.</summary>
internal sealed class WorkflowTestAdapter : ISystemAdapter
{
    public bool IsDryRunMode { get; set; } = true;
    public int PowerActions;
    public int DefaultAudioSwitches;
    public int DisplayActions;
    public float Volume = 0.5f;
    public bool OutputMuted;
    public bool InputMuted;
    public byte[] CaptureBytes = [];
    public System.Collections.Concurrent.ConcurrentQueue<string> Calls { get; } = new();
    public HashSet<string> RunningApps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsAppRunning(string executablePath) => RunningApps.Contains(executablePath);
    public List<ApplicationWindow> ApplicationWindows { get; } = [];
    public Exception? CloseFailure { get; set; }
    public IReadOnlyList<ApplicationWindow> GetApplicationWindows() => ApplicationWindows;
    public Task CloseApplicationsAsync(IReadOnlyList<ApplicationCloseTarget> targets, bool foregroundAtExecution, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Calls.Enqueue("CloseSelected");
        return CloseFailure is null ? Task.CompletedTask : Task.FromException(CloseFailure);
    }
    public Task LaunchAppAsync(LaunchAppOptions options, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Calls.Enqueue("Launch:" + options.ExecutablePath); return Task.CompletedTask; }
    public Task SetPowerStateAsync(PowerAction action, bool force = false, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref PowerActions); return Task.CompletedTask; }
    public Task SetDisplayPowerAsync(bool turnOn, CancellationToken cancellationToken = default) { Interlocked.Increment(ref DisplayActions); return Task.CompletedTask; }
    public Task SetMasterVolumeAsync(float volume, CancellationToken cancellationToken = default) { Volume = volume; Calls.Enqueue("Volume"); return Task.CompletedTask; }
    public Task SetMasterVolumeFadeAsync(float targetVolume, TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<float> GetMasterVolumeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Volume);
    public Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default) { OutputMuted = mute; Calls.Enqueue("OutputMute"); return Task.CompletedTask; }
    public Task<bool> GetMuteAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task RunKeepAliveLoopAsync(KeepAliveMode mode, int jitterMinSeconds, int jitterMaxSeconds, Action<string>? onActivityLogged, CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
    public void SimulateKeepAlivePulse(KeepAliveMode mode) { }
    public SystemMetrics GetCurrentMetrics() => new() { UserIdleSeconds = 0 };
    public float GetMasterPeakValue() => 0;
    public BatteryStatus GetBatteryStatus() => new();
    public Task<byte[]> CaptureScreenAsync(bool stampMetadata = false, string? label = null, CancellationToken cancellationToken = default) => Task.FromResult(CaptureBytes);
    public void SendMediaControl(bool pauseOnly = true) { }
    public AudioDeviceProfile GetAudioDevices() => new() { OutputId = "real-speakers", InputId = "real-mic",
        OutputName = "Altavoces predeterminados", InputName = "Micrófono predeterminado",
        OutputVolumePercent = (int)Math.Round(Volume * 100), OutputMuted = OutputMuted, InputMuted = InputMuted };
    public IReadOnlyList<AudioEndpointInfo> GetOutputAudioDevices() =>
        [new("unused-hdmi", "HDMI1", false), new("real-speakers", "Altavoces predeterminados", true)];
    public IReadOnlyList<AudioEndpointInfo> GetInputAudioDevices() => [new("real-mic", "Micrófono predeterminado", true)];
    public bool SetDefaultAudioDevice(string deviceId, bool input) { DefaultAudioSwitches++; return true; }
    public bool ToggleOutputMute() { OutputMuted = !OutputMuted; return true; }
    public bool ToggleInputMute() { InputMuted = !InputMuted; return true; }
    public bool SetInputMute(bool mute) { InputMuted = mute; Calls.Enqueue("InputMute"); return true; }
    public void Dispose() { }
}
