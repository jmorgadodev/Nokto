using System.Diagnostics;
using Microsoft.Win32;

namespace Nokto.Platform.Windows.Startup;

/// <summary>
/// Helper para registrar y desregistrar Nokto en el inicio de Windows vía HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class WindowsStartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Nokto";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStartup(bool enable, bool startMinimized = false, bool startInWorkMode = false)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(exePath)) return false;

                var args = new List<string>();
                if (startMinimized) args.Add("--silent");
                if (startInWorkMode) args.Add("--work");

                string cmd = $"\"{exePath}\"";
                if (args.Count > 0)
                {
                    cmd += " " + string.Join(" ", args);
                }

                key.SetValue(AppName, cmd);
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
