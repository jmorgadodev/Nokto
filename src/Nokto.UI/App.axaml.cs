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

            var config = persistence.LoadConfig();

            // Aplicar Idioma y Tema desde Config
            SetLanguage(config.Settings.Language);
            SetTheme(config.Settings.Theme, config.Settings.ScheduleDayTime, config.Settings.ScheduleNightTime);

            desktop.Exit += (s, e) =>
            {
                _isExiting = true;
                _scheduleThemeTimer?.Stop();
                // Remove the shell icon before releasing services: it must not outlive its UI.
                _trayIcon?.Dispose();
                _trayIcon = null;
                TrayIcon.SetIcons(this, new TrayIcons());
                _mainViewModel.Dispose();
                engine.Dispose();
                _systemAdapter?.Dispose();
            };

            _mainWindow = new MainWindow();
            try
            {
                _mainWindow.Icon = DynamicTrayIconRenderer.RenderAppWindowIcon();
            }
            catch { }
            _mainWindow.InitializeWithViewModel(_mainViewModel);
            desktop.MainWindow = _mainWindow;

            ConfigureTrayIcon(desktop);

            // Tarea 4: Soporte de argumentos de línea de comandos (Modo Sigiloso / Modo Trabajo)
            bool isSilent = config.Settings.StartMinimizedToTray || desktop.Args?.Any(a =>
                a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-s", StringComparison.OrdinalIgnoreCase)) == true;

            bool startWork = config.Settings.StartInWorkMode || desktop.Args?.Any(a =>
                a.Equals("--work", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-w", StringComparison.OrdinalIgnoreCase)) == true;

            if (!isSilent)
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Maximized;
                _mainWindow.Activate();
            }

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

        var itemOpen = new NativeMenuItem("Abrir Nokto");
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

        var itemExit = new NativeMenuItem("Salir de Nokto");
        itemExit.Click += (s, e) =>
        {
            _mainWindow?.ForceCloseApplication();
            desktop.Shutdown();
        };

        menu.Items.Add(itemOpen);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(itemDisplays);
        menu.Items.Add(itemPostpone);
        menu.Items.Add(itemFinish);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(itemExit);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Nokto — Sistema de Energía (Listo)",
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
                            _trayIcon.ToolTipText = state switch
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
