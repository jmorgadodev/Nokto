using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Persistence;
using Nokto.Platform.Windows;
using Nokto.UI.Tray;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

namespace Nokto.UI;

public partial class App : Application
{
    public static Avalonia.Styling.ThemeVariant SlateTheme { get; } = new("Slate", Avalonia.Styling.ThemeVariant.Dark);
    public static App? CurrentInstance { get; private set; }

    private ISystemAdapter? _systemAdapter;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private IslandWindow? _islandWindow;
    private TrayIcon? _trayIcon;
    private bool _trayUpdateFailureLogged;
    private bool _isExiting;
    private DispatcherTimer? _scheduleThemeTimer;
    private string _currentThemeMode = "Dark";
    private TimeSpan _scheduleDayTime = new(8, 0, 0);
    private TimeSpan _scheduleNightTime = new(20, 0, 0);

    public override void Initialize()
    {
        CurrentInstance = this;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var persistence = new PersistenceService();
            _systemAdapter = new WindowsSystemAdapter();
            var engine = new WorkflowEngine(_systemAdapter, persistence);
            _mainViewModel = new MainViewModel(_systemAdapter, engine, persistence);
            _mainViewModel.IslandVm.Changed += () => Dispatcher.UIThread.Post(SyncIslandVisibility);

            var config = persistence.LoadConfig();

            // Aplicar Idioma y Tema desde Config
            SetLanguage(config.Settings.Language);
            SetTheme(config.Settings.Theme, config.Settings.ScheduleDayTime, config.Settings.ScheduleNightTime);

            desktop.Exit += (s, e) =>
            {
                _isExiting = true;
                _scheduleThemeTimer?.Stop();
                _islandWindow?.Close();
                _islandWindow = null;
                // Remove the shell icon before releasing services: it must not outlive its UI.
                _trayIcon?.Dispose();
                _trayIcon = null;
                TrayIcon.SetIcons(this, new TrayIcons());
                _mainViewModel.Dispose();
                engine.Dispose();
                _systemAdapter?.Dispose();
            };

            _mainWindow = new MainWindow();
            if (Nokto.Core.BuildIdentity.IsDevelopment)
                _mainWindow.Title = "Nokto 2.0 DEV — Consola local de pruebas";
            try
            {
                _mainWindow.Icon = DynamicTrayIconRenderer.RenderAppWindowIcon();
            }
            catch { }
            _mainWindow.InitializeWithViewModel(_mainViewModel);
            desktop.MainWindow = _mainWindow;

            ConfigureTrayIcon(desktop);

            // La seleccion de inicio de Isla Dinamica es independiente de Iniciar con Windows.
            // Los argumentos --tray / --silent prevalecen para permitir pruebas sin ventanas.
            bool forcedTray = desktop.Args?.Any(a =>
                a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-s", StringComparison.OrdinalIgnoreCase)) == true;
            string launchMode = config.Island?.StartupModeConfigured == true
                ? config.Island.StartupMode
                : config.Settings.StartMinimizedToTray ? "Tray" : "Dashboard";
            if (config.Island?.Enabled != true && (launchMode == "Island" || launchMode == "Both"))
                launchMode = "Dashboard";
            bool isSilent = forcedTray || launchMode is "Island" or "Tray";

            bool startWork = config.Settings.StartInWorkMode || desktop.Args?.Any(a =>
                a.Equals("--work", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-w", StringComparison.OrdinalIgnoreCase)) == true;

            if (isSilent)
            {
                // Avalonia puede abrir MainWindow automaticamente al iniciar el lifetime.
                // Ocultarla en Opened evita que aparezca de forma persistente en Isla/Bandeja.
                bool hideInitialOpen = true;
                _mainWindow.Opened += (_, _) =>
                {
                    if (!hideInitialOpen) return;
                    hideInitialOpen = false;
                    _mainWindow.Hide();
                };
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Maximized;
                _mainWindow.Activate();
            }

            if (!forcedTray && config.Island?.Enabled == true &&
                (launchMode == "Island" || launchMode == "Both"))
                ShowIslandWindow();

            if (startWork)
            {
                _mainViewModel.ToggleKeepAliveCommand.Execute(null);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void SetLanguage(string language)
    {
        string uri = language == "en"
            ? "avares://Nokto/Resources/Locale.en.axaml"
            : "avares://Nokto/Resources/Locale.es.axaml";

        try
        {
            var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri));
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(dict);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error switching locale: {ex.Message}");
        }
    }

    public void SetTheme(string themeMode, TimeSpan dayTime, TimeSpan nightTime)
    {
        _currentThemeMode = themeMode;
        _scheduleDayTime = dayTime;
        _scheduleNightTime = nightTime;

        ApplyCurrentTheme();

        if (themeMode == "Schedule")
        {
            if (_scheduleThemeTimer == null)
            {
                _scheduleThemeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                _scheduleThemeTimer.Tick += (s, e) =>
                {
                    if (_currentThemeMode == "Schedule")
                    {
                        ApplyCurrentTheme();
                    }
                };
            }
            if (!_scheduleThemeTimer.IsEnabled)
            {
                _scheduleThemeTimer.Start();
            }
        }
        else
        {
            _scheduleThemeTimer?.Stop();
        }
    }

    private void ApplyCurrentTheme()
    {
        switch (_currentThemeMode)
        {
            case "Light":
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                break;
            case "Dark":
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                break;
            case "Slate":
                RequestedThemeVariant = SlateTheme;
                break;
            case "Schedule":
                var now = DateTime.Now.TimeOfDay;
                bool isDay = now >= _scheduleDayTime && now < _scheduleNightTime;
                RequestedThemeVariant = isDay ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                break;
            case "Windows":
            default:
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                break;
        }
    }

    private void ConfigureTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var itemOpen = new NativeMenuItem(Nokto.Core.BuildIdentity.IsDevelopment ? "Abrir Nokto DEV" : "Abrir Nokto");
        itemOpen.Click += (s, e) => ShowMainWindow();

        var itemDisplays = new NativeMenuItem("🌙 Apagar Monitores Ahora");
        itemDisplays.Click += async (s, e) =>
        {
            if (_systemAdapter != null)
            {
                await _systemAdapter.SetDisplayPowerAsync(false);
            }
        };

        var itemPostpone = new NativeMenuItem("⏱ +15 Minutos");
        itemPostpone.Click += (s, e) => _mainViewModel?.PostponeTask("15");

        var itemFinish = new NativeMenuItem("⏹ Finalizar todas las rutinas");
        itemFinish.Click += (s, e) => _mainViewModel?.FinishTask();

        var itemExit = new NativeMenuItem(Nokto.Core.BuildIdentity.IsDevelopment ? "Salir de Nokto DEV" : "Salir de Nokto");
        itemExit.Click += (s, e) =>
        {
            _mainWindow?.ForceCloseApplication();
            desktop.Shutdown();
        };

        menu.Items.Add(itemOpen);
        var itemIsland = new NativeMenuItem("Mostrar / ocultar Isla Dinámica");
        itemIsland.Click += (_, _) =>
        {
            if (_mainViewModel != null)
                _mainViewModel.IslandVm.Enabled = !_mainViewModel.IslandVm.Enabled;
        };
        menu.Items.Add(itemIsland);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(itemDisplays);
        menu.Items.Add(itemPostpone);
        menu.Items.Add(itemFinish);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(itemExit);

        _trayIcon = new TrayIcon
        {
            ToolTipText = Nokto.Core.BuildIdentity.IsDevelopment ? "Nokto 2.0 DEV (local)" : "Nokto — Sistema de Energía (Listo)",
            Menu = menu,
            IsVisible = true,
            Icon = DynamicTrayIconRenderer.RenderTrayIcon(TrayIconVisualState.Idle)
        };

        _trayIcon.Clicked += (s, e) => ShowMainWindow();

        var icons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, icons);

        if (_mainViewModel != null)
        {
            _mainViewModel.RequestTrayIconUpdate += (state, progress, secondsRemaining, pulsePhase) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_isExiting && _trayIcon != null)
                    {
                        try
                        {
                            _trayIcon.Icon = DynamicTrayIconRenderer.RenderTrayIcon(state, progress, secondsRemaining, pulsePhase);
                            _trayIcon.ToolTipText = Nokto.Core.BuildIdentity.IsDevelopment
                                ? "Nokto 2.0 DEV — Entorno local"
                                : state switch
                                {
                                    TrayIconVisualState.InProgress => $"Nokto: {progress:F0}% ({secondsRemaining}s)",
                                    TrayIconVisualState.Completed => "Nokto: Tarea completada con éxito",
                                    _ => "Nokto — Sistema de Energía (Listo)"
                                };
                        }
                        catch (System.ComponentModel.Win32Exception ex)
                        {
                            // A shell icon failure must not terminate the dashboard or active routines.
                            if (!_trayUpdateFailureLogged)
                            {
                                _trayUpdateFailureLogged = true;
                                try
                                {
                                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "tray-error.log"),
                                        $"[{DateTime.UtcNow:O}] {ex}{Environment.NewLine}");
                                }
                                catch { }
                            }
                        }
                    }
                });
            };
        }
    }

    private void SyncIslandVisibility()
    {
        if (_isExiting || _mainViewModel == null) return;
        if (_mainViewModel.IslandVm.Enabled)
            ShowIslandWindow();
        else
        {
            _islandWindow?.Close();
            _islandWindow = null;
        }
    }

    private void ShowIslandWindow()
    {
        if (_isExiting || _mainViewModel == null) return;
        _islandWindow ??= new IslandWindow(_mainViewModel.IslandVm);
        _islandWindow.ApplySettings();
        if (!_islandWindow.IsVisible)
            _islandWindow.Show();
    }

    public void ShowMainWindow()
    {
        try
        {
            var window = _mainWindow;
            if (_isExiting || window is null || window.IsExiting || window.PlatformImpl is null) return;
            // Restore the state before Show; a minimized window hides itself when shown.
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Maximized;
            if (!window.IsVisible)
            {
                window.Show();
            }
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }
        catch (InvalidOperationException)
        {
            // A queued tray click can arrive while the native window is being destroyed.
        }
    }
}
