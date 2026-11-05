namespace Nokto.Core.Models;

/// <summary>Resuelve cómo se inicia Nokto sin que los argumentos antiguos oculten una isla elegida.</summary>
public sealed record IslandStartupPlan(string Mode, bool HideDashboard, bool ShowIsland, bool ForceTray);

public static class IslandStartupPolicy
{
    public static IslandStartupPlan Resolve(AppConfig config, IEnumerable<string>? arguments = null)
    {
        var args = arguments?.ToArray() ?? [];
        bool forceTray = args.Any(argument => argument.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        bool legacySilent = args.Any(argument =>
            argument.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-s", StringComparison.OrdinalIgnoreCase));

        string mode = config.Island.StartupModeConfigured
            ? config.Island.StartupMode
            : config.Settings.StartMinimizedToTray ? "Tray" : "Dashboard";
        string selectedMode = mode;
        if (!config.Island.Enabled && (mode is "Island" or "Both")) mode = "Dashboard";

        bool showIsland = !forceTray && config.Island.Enabled && (selectedMode is "Island" or "Both");
        bool silentArgumentApplies = legacySilent && selectedMode is not ("Island" or "Both");
        bool hideDashboard = forceTray || silentArgumentApplies || (mode is "Island" or "Tray");
        return new IslandStartupPlan(mode, hideDashboard, showIsland, forceTray);
    }
}
