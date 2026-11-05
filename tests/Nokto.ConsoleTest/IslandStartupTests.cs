using Nokto.Core.Models;

namespace Nokto.ConsoleTest;

internal static class IslandStartupTests
{
    public static int Run()
    {
        try
        {
            var config = new AppConfig();
            config.Island.Enabled = true;
            config.Island.StartupModeConfigured = true;
            config.Island.StartupMode = "Island";
            var legacySilentIsland = IslandStartupPolicy.Resolve(config, ["--silent"]);
            Require(legacySilentIsland.HideDashboard && legacySilentIsland.ShowIsland,
                "El argumento de bandeja antiguo no debe anular el inicio explícito de la isla.");

            var both = new AppConfig { Island = new IslandSettings { Enabled = true, StartupModeConfigured = true, StartupMode = "Both" } };
            var bothPlan = IslandStartupPolicy.Resolve(both);
            Require(!bothPlan.HideDashboard && bothPlan.ShowIsland, "El modo Ambos muestra el panel y la isla.");

            var trayPlan = IslandStartupPolicy.Resolve(config, ["--tray"]);
            Require(trayPlan.ForceTray && trayPlan.HideDashboard && !trayPlan.ShowIsland,
                "La opción explícita --tray mantiene solo la bandeja.");

            config.Settings.StartMinimizedToTray = true;
            config.Island.StartupModeConfigured = false;
            var legacyPlan = IslandStartupPolicy.Resolve(config, ["--silent"]);
            Require(legacyPlan.Mode == "Tray" && legacyPlan.HideDashboard && !legacyPlan.ShowIsland,
                "Las instalaciones sin preferencia nueva conservan el inicio antiguo en bandeja.");

            config.Island.StartupModeConfigured = true;
            config.Island.StartupMode = "Island";
            config.Island.Enabled = false;
            var disabled = IslandStartupPolicy.Resolve(config);
            Require(disabled.Mode == "Dashboard" && !disabled.ShowIsland,
                "Una isla deshabilitada no se inicia por accidente.");

            var disabledLegacy = IslandStartupPolicy.Resolve(config, ["--silent"]);
            Require(!disabledLegacy.HideDashboard && !disabledLegacy.ShowIsland,
                "Una preferencia heredada silenciosa no debe dejar Nokto sin ventana si la isla está deshabilitada.");
            Console.WriteLine("[PASS] Inicio de la isla: argumentos heredados, modo Ambos, bandeja explícita y configuración anterior.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] Island startup: " + ex.Message);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
