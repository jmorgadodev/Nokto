using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nokto.Platform.Windows.Hardware;

public sealed record GraphicsAdapterIdentity(string Name, ulong DedicatedVideoMemoryBytes);

/// <summary>Enumerates physical DXGI adapters, including GPUs without a connected display.</summary>
public static class GraphicsAdapterDiscovery
{
    private static readonly Guid FactoryId = new("7b7166ec-21c7-44ae-b21a-c9ae321ae369");

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory(in Guid riid, out IntPtr factory);

    public static unsafe IReadOnlyList<GraphicsAdapterIdentity> Enumerate()
    {
        var adapters = new List<GraphicsAdapterIdentity>();
        IntPtr factory = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(CreateDXGIFactory(in FactoryId, out factory));
            // IUnknown (3), IDXGIObject (4), then IDXGIFactory.EnumAdapters (slot 7).
            var enumerate = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)factory)[7];
            for (uint index = 0; index < 128; index++)
            {
                IntPtr adapter = IntPtr.Zero;
                int result = enumerate(factory, index, &adapter);
                if (result == unchecked((int)0x887A0002)) break; // DXGI_ERROR_NOT_FOUND
                try
                {
                    Marshal.ThrowExceptionForHR(result);
                    // IDXGIAdapter.EnumOutputs (slot 7), then GetDesc (slot 8).
                    var getDescription = (delegate* unmanaged[Stdcall]<IntPtr, AdapterDescription*, int>)(*(IntPtr**)adapter)[8];
                    AdapterDescription description = default;
                    Marshal.ThrowExceptionForHR(getDescription(adapter, &description));
                    string name = new string(description.Description, 0, 128).TrimEnd('\0').Trim();
                    adapters.Add(new GraphicsAdapterIdentity(name, (ulong)description.DedicatedVideoMemory));
                }
                finally { if (adapter != IntPtr.Zero) Marshal.Release(adapter); }
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine("Nokto: DXGI hardware identity unavailable; retaining the display adapter fallback.");
        }
        finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
        return adapters;
    }

    public static string FormatNames(IEnumerable<GraphicsAdapterIdentity> adapters, string fallback)
    {
        var names = adapters.Where(a => !string.IsNullOrWhiteSpace(a.Name) &&
                !a.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("Software", StringComparison.OrdinalIgnoreCase) &&
                !a.Name.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.DedicatedVideoMemoryBytes)
            .Select(a => a.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Length > 0 ? string.Join(" + ", names) : string.IsNullOrWhiteSpace(fallback) ? "No disponible" : fallback;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
    }
}
