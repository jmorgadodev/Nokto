using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Nokto.Platform.Windows.Applications;

namespace Nokto.ConsoleTest;

internal static class InstalledAppsTests
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flag);
    public static async Task<int> RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "nokto_catalog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string target = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            WriteShortcut(Path.Combine(root, "Editor.lnk"), target, "/c echo catalog-fixture");
            WriteShortcut(Path.Combine(root, "Duplicate.lnk"), target, "/c echo catalog-fixture");
            foreach (string name in new[] { "Uninstall", "DESINSTALAR", "readme", "help" }) WriteShortcut(Path.Combine(root, name + ".lnk"), target, "");
            File.WriteAllText(Path.Combine(root, "Web.url"), "[InternetShortcut]\nURL=https://example.invalid");
            File.WriteAllText(Path.Combine(root, "Broken.lnk"), "invalid shortcut");
            var service = new InstalledAppsService([root]);
            var apps = await service.ScanAsync();
            Require(apps.Count == 1 && apps[0].ExecutablePath.Equals(target, StringComparison.OrdinalIgnoreCase), "Filtro de ayudas, desinstaladores, .url, enlaces corruptos y duplicados.");
            Require(apps[0].Arguments == "/c echo catalog-fixture" && apps[0].WorkingDirectory == Environment.SystemDirectory, "Argumentos y directorio del acceso directo.");
            Require(apps[0].IconPixels is { Length: 4096 } pixels && pixels.Any(b => b != 0), "Icono nativo de 32×32 píxeles BGRA.");
            Require(ReferenceEquals(apps, await service.ScanAsync()), "Catálogo e iconos en caché.");
            using var current = Process.GetCurrentProcess();
            Require(InstalledAppsService.IsAppRunning(current.MainModule!.FileName) && !InstalledAppsService.IsAppRunning(Guid.NewGuid() + ".exe"), "Detección de proceso por ejecutable.");
            uint gdi = GetGuiResources(current.Handle, 0), user = GetGuiResources(current.Handle, 1);
            for (int i = 0; i < 40; i++) await new InstalledAppsService([root]).ScanAsync();
            uint afterGdi = GetGuiResources(current.Handle, 0), afterUser = GetGuiResources(current.Handle, 1);
            Require(afterGdi <= gdi + 2 && afterUser <= user + 2, $"Fuga de iconos: GDI {gdi}→{afterGdi}, USER {user}→{afterUser}.");
            Console.WriteLine($"[PASS] Catálogo: filtros, argumentos, iconos 32×32, caché y procesos; GDI {gdi}→{afterGdi}, USER {user}→{afterUser}.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[FAIL] Catálogo local: " + ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
    private static void WriteShortcut(string path, string executable, string arguments)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
            shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path])!;
            foreach (var (name, value) in new[] { ("TargetPath", executable), ("Arguments", arguments), ("WorkingDirectory", Environment.SystemDirectory) })
                shortcut.GetType().InvokeMember(name, BindingFlags.SetProperty, null, shortcut, [value]);
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally { if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
