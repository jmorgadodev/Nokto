namespace Nokto.Core.Models;

public sealed class IslandSettings
{
    public bool Enabled { get; set; } = false;
    public string Style { get; set; } = "Eyes";
    // 80..140 %, 100 % corresponde al tamaño anterior.
    public int SizePercent { get; set; } = 100;
    public bool ShowFileShelf { get; set; } = false;
    // Solo referencias locales seleccionadas por el usuario. Nunca copiar ni subir contenido.
    public List<string> ShelfFiles { get; set; } = [];
    public bool ShowQuickActions { get; set; } = true;
    public bool EnableWheelPages { get; set; } = true;
    public List<string> QuickFolders { get; set; } = [];
    public bool ShowClipboardActions { get; set; } = false;
    // Preferencias independientes por página. Defaults conservan versiones anteriores.
    public bool ShowOverviewPage { get; set; } = true;
    public bool ShowSoundPage { get; set; } = true;
    public bool ShowShortcutsPage { get; set; } = true;
    public bool ShowMainButton { get; set; } = true;
    public bool ShowHideButton { get; set; } = true;
    public bool SoundShowVolume { get; set; } = true;
    public bool SoundShowMicrophone { get; set; } = true;
    public bool SoundShowOutputMute { get; set; } = true;
    public bool ShortcutsShowRoutines { get; set; } = true;
    public bool ShortcutsShowExplorer { get; set; } = true;
    public bool ShortcutsShowFolders { get; set; } = true;
    public bool ShowWeather { get; set; } = false;
    public string WeatherCity { get; set; } = "";
    public bool ShowWindowsNotifications { get; set; } = false;
    public bool PrivacyMode { get; set; } = false;
    // Dashboard (legacy default), Island, Both, Tray.
    public string StartupMode { get; set; } = "Dashboard";
    // Los usuarios anteriores conservan su ajuste de inicio hasta elegir un modo nuevo.
    public bool StartupModeConfigured { get; set; } = false;
    public bool ShowClock { get; set; } = true;
    // Auto migra el ajuste anterior: activado => Expand, desactivado => Never.
    public string ClockVisibility { get; set; } = "Auto";
    public bool ShowAudioControls { get; set; } = true;
    public bool ShowBatteryStatus { get; set; } = true;
    public bool ShowNetworkStatus { get; set; } = false;
    public bool ShowMediaControls { get; set; } = true;
    public bool ShowMediaInCompact { get; set; } = false;
    public bool ExpandOnHover { get; set; } = true;
    public bool ReducedMotion { get; set; } = false;
    // Auto usa la frecuencia actual del monitor; valores manuales: 60,120,144,165,240.
    public string AnimationRateMode { get; set; } = "Auto";
    public bool FollowCursor { get; set; } = true;
    public bool NaturalBlink { get; set; } = true;
    public bool SleepOnIdle { get; set; } = true;
    public double EyeSensitivity { get; set; } = 0.65;
    public IslandProductivityState Productivity { get; set; } = new();
}