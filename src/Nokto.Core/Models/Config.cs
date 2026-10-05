namespace Nokto.Core.Models;

public record AppConfig
{
    public string? Schema { get; set; } = "https://raw.githubusercontent.com/nokto/schemas/v1/config.schema.json";
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public AppSettings App { get => Settings; set => Settings = value; }
    public KeepAliveSettings KeepAlive { get; set; } = new();
    public LanServerSettings LanServer { get; set; } = new();
}

public record AppSettings
{
    public string Language { get; set; } = "es";
    public string Theme { get; set; } = "Dark";
    public TimeSpan ScheduleDayTime { get; set; } = new(8, 0, 0);
    public TimeSpan ScheduleNightTime { get; set; } = new(20, 0, 0);
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool StartInWorkMode { get; set; } = false;
    public int GracePeriodSeconds { get; set; } = 60;
    public string PanicHotkey { get; set; } = "Pause";
    public string MicMuteHotkey { get; set; } = "Ctrl+Shift+M";
    public string AudioMuteHotkey { get; set; } = "Ctrl+Shift+S";
    public int MetricsPollingIntervalMs { get; set; } = 1000;
    public bool BatteryProtectionEnabled { get; set; } = false;
    public int BatteryThresholdPercent { get; set; } = 10;
    public string BatteryAction { get; set; } = "Hibernate";
    public bool EvidenceScreenshotsEnabled { get; set; } = true;
    public int MaxEvidenceRetention { get; set; } = 20;
    public int DiskAlertThresholdGb { get; set; } = 15;
    public bool ShowNetworkCardInHome { get; set; } = true;
    public bool ShowEnergyStatusCardInHome { get; set; } = true;
    public bool ShowHardwareCardInHome { get; set; } = true;
    public bool ShowAiRadarCardInHome { get; set; } = true;
    public bool ShowQuickActionsInHome { get; set; } = true;
    public bool ShowAudioControlCardInHome { get; set; } = true;
    public List<string> HiddenAiEnvironmentIds { get; set; } = [];
    public List<string> AiEnvironmentOrder { get; set; } = ["codex"];
    // Keep existing portable configurations and older clients compatible.
    public bool ShowAiRadarInHome { get => ShowAiRadarCardInHome; set => ShowAiRadarCardInHome = value; }
}

public record KeepAliveSettings
{
    public KeepAliveMode DefaultMode { get; set; } = KeepAliveMode.InputSimulation;
    public int JitterMinSeconds { get; set; } = 45;
    public int JitterMaxSeconds { get; set; } = 105;
    public string SimulatedKey { get; set; } = "VK_F15";
    public int MouseDeltaPixels { get; set; } = 1;
}

public record LanServerSettings
{
    public bool Enabled { get; set; } = false;
    public int Port { get; set; } = 5050;
    public string BindAddress { get; set; } = "0.0.0.0";
    public bool RequireAuth { get; set; } = true;
    public string AuthToken { get; set; } = "a9f82d1c6e4b8a73";
    public bool AllowScreenPreview { get; set; } = true;
    public int ScreenPreviewQuality { get; set; } = 60;
}
