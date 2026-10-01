using System.Runtime.InteropServices;

namespace Nokto.Platform.Windows.Audio;

internal enum EDataFlow
{
    eRender,
    eCapture,
    eAll
}

internal enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr pClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        [In] ref Guid iid,
        [In] uint dwClsCtx,
        [In] IntPtr pActivationParams,
        [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    [PreserveSig]
    int GetState(out uint pdwState);
}

[ComImport]
[Guid("5BC69FDE-8A07-440E-A43D-64F0F0748237")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int GetChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float fLevelDB, [In] ref Guid pguidEventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float fLevel, [In] ref Guid pguidEventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float pfLevelDB);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float pfLevel);

    [PreserveSig]
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, [In] ref Guid pguidEventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, [In] ref Guid pguidEventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, [In] ref Guid pguidEventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);

    [PreserveSig]
    int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);

    [PreserveSig]
    int VolumeStepUp([In] ref Guid pguidEventContext);

    [PreserveSig]
    int VolumeStepDown([In] ref Guid pguidEventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

[ComImport]
[Guid("C02216F6-0388-4E45-9285-18B42C1B15F9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float pfPeak);

    [PreserveSig]
    int GetMeteringChannelCount(out uint pnChannelCount);

    [PreserveSig]
    int GetChannelsPeakValues(uint u32ChannelCount, [In, Out] float[] afPeakValues);

    [PreserveSig]
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);
}

/// <summary>
/// Controlador WASAPI de bajo nivel para el volumen de audio maestro del sistema y medición de picos (metering).
/// Sin dependencias pesadas de terceros.
/// </summary>
public sealed class WasapiAudioController : IDisposable
{
    private const uint CLSCTX_INPROC_SERVER = 1;
    private static readonly Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
    private static readonly Guid IID_IAudioMeterInformation = typeof(IAudioMeterInformation).GUID;

    private IAudioEndpointVolume? _endpointVolume;
    private IAudioMeterInformation? _audioMeter;
    private bool _disposed;

    public WasapiAudioController()
    {
        InitializeVolumeEndpoint();
    }

    private void InitializeVolumeEndpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
            if (hr != 0 || device == null)
            {
                // Fallback a eConsole si no hay multimedia
                hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out device);
            }

            if (hr == 0 && device != null)
            {
                var iidVol = IID_IAudioEndpointVolume;
                hr = device.Activate(ref iidVol, CLSCTX_INPROC_SERVER, IntPtr.Zero, out var audioObj);
                if (hr == 0 && audioObj is IAudioEndpointVolume volume)
                {
                    _endpointVolume = volume;
                }

                var iidMeter = IID_IAudioMeterInformation;
                hr = device.Activate(ref iidMeter, CLSCTX_INPROC_SERVER, IntPtr.Zero, out var meterObj);
                if (hr == 0 && meterObj is IAudioMeterInformation meter)
                {
                    _audioMeter = meter;
                }
            }
        }
        catch
        {
            // Silently handle systems without audio endpoints
            _endpointVolume = null;
            _audioMeter = null;
        }
    }

    /// <summary>
    /// Obtiene el nivel de pico maestro actual [0.0f - 1.0f] mediante IAudioMeterInformation.
    /// Si el sistema carece de audio o no hay actividad, retorna 0.0f.
    /// </summary>
    public float GetPeakValue()
    {
        if (_audioMeter == null) return 0f;
        int hr = _audioMeter.GetPeakValue(out float peak);
        return hr == 0 ? Math.Clamp(peak, 0f, 1f) : 0f;
    }

    public float GetMasterVolume()
    {
        if (_endpointVolume == null) return 0f;
        int hr = _endpointVolume.GetMasterVolumeLevelScalar(out float level);
        return hr == 0 ? Math.Clamp(level, 0f, 1f) : 0f;
    }

    public void SetMasterVolume(float volume)
    {
        if (_endpointVolume == null) return;
        float clamped = Math.Clamp(volume, 0f, 1f);
        var context = Guid.Empty;
        _endpointVolume.SetMasterVolumeLevelScalar(clamped, ref context);
    }

    public bool GetMute()
    {
        if (_endpointVolume == null) return false;
        int hr = _endpointVolume.GetMute(out bool isMuted);
        return hr == 0 && isMuted;
    }

    public void SetMute(bool mute)
    {
        if (_endpointVolume == null) return;
        var context = Guid.Empty;
        _endpointVolume.SetMute(mute, ref context);
    }

    /// <summary>
    /// Desvanece el volumen de manera suave mediante una función perceptual cuadrática/logarítmica.
    /// Utiliza PeriodicTimer sin busy-waiting.
    /// </summary>
    public async Task FadeVolumeAsync(float targetVolume, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (_endpointVolume == null) return;

        float targetClamped = Math.Clamp(targetVolume, 0f, 1f);
        float initialVolume = GetMasterVolume();

        if (Math.Abs(initialVolume - targetClamped) < 0.001f || duration <= TimeSpan.Zero)
        {
            SetMasterVolume(targetClamped);
            return;
        }

        // Convertimos a espacio perceptual (raíz cuadrada para volumen percibido por el oído humano)
        float initialPerceptual = (float)Math.Sqrt(initialVolume);
        float targetPerceptual = (float)Math.Sqrt(targetClamped);

        const int stepIntervalMs = 50;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(stepIntervalMs));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (!cancellationToken.IsCancellationRequested && stopwatch.Elapsed < duration)
        {
            await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);

            double progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0.0, 1.0);
            
            // Interpolación en espacio perceptual
            float currentPerceptual = (float)(initialPerceptual + (targetPerceptual - initialPerceptual) * progress);
            
            // Regreso a espacio de ganancia escalar cuadrático
            float currentScalar = Math.Clamp(currentPerceptual * currentPerceptual, 0f, 1f);

            SetMasterVolume(currentScalar);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            SetMasterVolume(targetClamped);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_audioMeter != null && Marshal.IsComObject(_audioMeter))
            {
                Marshal.ReleaseComObject(_audioMeter);
                _audioMeter = null;
            }
            if (_endpointVolume != null && Marshal.IsComObject(_endpointVolume))
            {
                Marshal.ReleaseComObject(_endpointVolume);
                _endpointVolume = null;
            }
            _disposed = true;
        }
    }
}
