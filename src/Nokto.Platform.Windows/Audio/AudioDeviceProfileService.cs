using System.Runtime.InteropServices;
using Nokto.Core.Models;

namespace Nokto.Platform.Windows.Audio;

public static class AudioDeviceProfileService
{
    private static readonly PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    public static AudioDeviceProfile Capture()
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var output = ReadEndpoint(enumerator, EDataFlow.eRender);
            var input = ReadEndpoint(enumerator, EDataFlow.eCapture);
            return new AudioDeviceProfile
            {
                OutputName = output.Name,
                InputName = input.Name,
                OutputVolumePercent = output.VolumePercent,
                OutputMuted = output.Muted,
                InputMuted = input.Muted,
                OutputId = output.Id,
                InputId = input.Id
            };
        }
        catch (COMException) { return new(); }
        finally { if (enumerator != null) Marshal.ReleaseComObject(enumerator); }
    }

    private readonly record struct EndpointSnapshot(string Name, int? VolumePercent = null, bool? Muted = null, string? Id = null);

    public static IReadOnlyList<AudioEndpointInfo> Enumerate(bool input)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;
        IntPtr collectionPointer = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var direction = input ? EDataFlow.eCapture : EDataFlow.eRender;
            string? defaultId = null;
            int hr = enumerator.GetDefaultAudioEndpoint(direction, ERole.eMultimedia, out defaultDevice);
            if (hr < 0) hr = enumerator.GetDefaultAudioEndpoint(direction, ERole.eConsole, out defaultDevice);
            if (hr >= 0 && defaultDevice != null && defaultDevice.GetId(out string id) >= 0) defaultId = id;
            if (enumerator.EnumAudioEndpoints(direction, 1, out collectionPointer) < 0) return [];
            collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collectionPointer);
            if (collection.GetCount(out uint count) < 0) return [];
            var result = new List<AudioEndpointInfo>();
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(i, out device) >= 0 && device.GetId(out string deviceId) >= 0)
                        result.Add(new(deviceId, ReadFriendlyName(device), deviceId == defaultId));
                }
                finally { if (device != null) Marshal.ReleaseComObject(device); }
            }
            return result.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (COMException) { return []; }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            if (collectionPointer != IntPtr.Zero) Marshal.Release(collectionPointer);
            if (defaultDevice != null) Marshal.ReleaseComObject(defaultDevice);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public static bool? GetMute(bool input) => WithVolume<bool?>(input, v => v.GetMute(out bool muted) >= 0 ? muted : null);
    public static bool ToggleMute(bool input) => WithVolume(input, v =>
    {
        if (v.GetMute(out bool muted) < 0) return false;
        var context = Guid.Empty;
        return v.SetMute(!muted, ref context) >= 0;
    });
    public static bool SetMute(bool input, bool muted) => WithVolume(input, v =>
    {
        var context = Guid.Empty;
        return v.SetMute(muted, ref context) >= 0;
    });
    public static float GetMasterVolume() => WithVolume(false, v => v.GetMasterVolumeLevelScalar(out float level) >= 0 ? Math.Clamp(level, 0, 1) : 0);
    public static bool SetMasterVolume(float level) => WithVolume(false, v =>
    {
        if (!float.IsFinite(level)) return false;
        var context = Guid.Empty;
        return v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 1), ref context) >= 0;
    });

    private static T? WithVolume<T>(bool input, Func<IAudioEndpointVolume, T> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? volumeObject = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var direction = input ? EDataFlow.eCapture : EDataFlow.eRender;
            int hr = enumerator.GetDefaultAudioEndpoint(direction, ERole.eMultimedia, out device);
            if (hr < 0) hr = enumerator.GetDefaultAudioEndpoint(direction, ERole.eConsole, out device);
            if (hr < 0 || device == null) return default;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 1, IntPtr.Zero, out volumeObject) < 0 || volumeObject is not IAudioEndpointVolume volume) return default;
            return action(volume);
        }
        catch (COMException) { return default; }
        finally
        {
            if (volumeObject != null && Marshal.IsComObject(volumeObject)) Marshal.ReleaseComObject(volumeObject);
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static string ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        IntPtr properties = IntPtr.Zero;
        PropVariant value = default;
        try
        {
            if (device.OpenPropertyStore(0, out properties) < 0) return "Dispositivo de audio";
            store = (IPropertyStore)Marshal.GetObjectForIUnknown(properties);
            if (store.GetValue(in FriendlyName, out value) < 0 || value.Type != 31) return "Dispositivo de audio";
            string? name = Marshal.PtrToStringUni(value.Data.Pointer);
            return string.IsNullOrWhiteSpace(name) ? "Dispositivo de audio" : name.Trim();
        }
        finally
        {
            PropVariantClear(ref value);
            if (store != null) Marshal.ReleaseComObject(store);
            if (properties != IntPtr.Zero) Marshal.Release(properties);
        }
    }

    private static EndpointSnapshot ReadEndpoint(IMMDeviceEnumerator enumerator, EDataFlow direction)
    {
        IMMDevice? device = null;
        object? volumeObject = null;
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(direction, ERole.eMultimedia, out device);
            if (hr < 0) hr = enumerator.GetDefaultAudioEndpoint(direction, ERole.eConsole, out device);
            if (hr < 0 || device == null || device.GetState(out uint state) < 0 || (state & 1) == 0) return new("Sin dispositivo activo");
            string name = ReadFriendlyName(device);
            string? id = device.GetId(out string deviceId) >= 0 ? deviceId : null;
            int? percent = null;
            bool? muted = null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 1, IntPtr.Zero, out volumeObject) >= 0 && volumeObject is IAudioEndpointVolume volume)
            {
                if (volume.GetMute(out bool isMuted) >= 0) muted = isMuted;
                if (direction == EDataFlow.eRender && volume.GetMasterVolumeLevelScalar(out float scalar) >= 0 && float.IsFinite(scalar))
                    percent = (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100, MidpointRounding.AwayFromZero);
            }
            return new(name, percent, muted, id);
        }
        catch (COMException) { return new("Sin dispositivo activo"); }
        finally
        {
            if (volumeObject != null && Marshal.IsComObject(volumeObject)) Marshal.ReleaseComObject(volumeObject);
            if (device != null) Marshal.ReleaseComObject(device);
        }
    }

    [ComImport, Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type, Reserved1, Reserved2, Reserved3;
        public VariantData Data;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct VariantData
    {
        [FieldOffset(0)] public IntPtr Pointer;
        [FieldOffset(0)] public Blob Blob;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public uint Size; public IntPtr Data; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);
        [PreserveSig] int Commit();
    }
}
