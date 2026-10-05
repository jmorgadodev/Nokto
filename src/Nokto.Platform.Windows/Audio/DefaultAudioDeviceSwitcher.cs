using System.Runtime.InteropServices;

namespace Nokto.Platform.Windows.Audio;

/// <summary>Isolates Windows' undocumented IPolicyConfig compatibility boundary.</summary>
internal static class DefaultAudioDeviceSwitcher
{
    public static unsafe bool Set(string deviceId, bool input)
    {
        if (!AudioDeviceProfileService.Enumerate(input).Any(d => d.Id == deviceId)) return false;
        IMMDeviceEnumerator? enumerator = null;
        IntPtr policy = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            var previous = new string?[3];
            var direction = input ? EDataFlow.eCapture : EDataFlow.eRender;
            for (int role = 0; role < 3; role++)
            {
                IMMDevice? device = null;
                try
                {
                    if (enumerator.GetDefaultAudioEndpoint(direction, (ERole)role, out device) >= 0 && device.GetId(out string id) >= 0)
                        previous[role] = id;
                }
                finally { if (device != null) Marshal.ReleaseComObject(device); }
            }
            var clsid = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");
            var iid = new Guid("f8679f50-850a-41cf-9c72-430f290290c8");
            if (CoCreateInstance(in clsid, IntPtr.Zero, 0x17, in iid, out policy) < 0) return false;
            var setDefault = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)(*(IntPtr**)policy)[13];
            fixed (char* id = deviceId)
                for (int role = 0; role < 3; role++)
                {
                    if (setDefault(policy, id, role) >= 0) continue;
                    for (int rollback = 0; rollback < role; rollback++)
                        if (previous[rollback] is string original)
                            fixed (char* oldId = original) setDefault(policy, oldId, rollback);
                    return false;
                }
            return true;
        }
        catch (COMException) { return false; }
        finally
        {
            if (policy != IntPtr.Zero) Marshal.Release(policy);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr result);
}
