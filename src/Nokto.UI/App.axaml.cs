using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Nokto.Core.Abstractions;
using Nokto.Core.Engine;
using Nokto.Core.Persistence;
using Nokto.LanServer;
using Nokto.Platform.Windows;
using Nokto.UI.Tray;
using Nokto.UI.ViewModels;
using Nokto.UI.Views;

namespace Nokto.UI;

public partial class App : Application
{
    public static App? CurrentInstance { get; private set; }

    private ISystemAdapter? _systemAdapter;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private LanHttpServer? _lanServer;

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

            try
            {
                var config = persistence.LoadConfig();
                _lanServer = new LanHttpServer(engine, _systemAdapter, config.LanServer);
                _lanServer.Start();
                _mainViewModel.LanConnectionUrl = _lanServer.GetConnectionUrl();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Microservidor LAN no disponible: {ex.Message}");
            }

            desktop.Exit += (s, e) =>
            {
                _lanServer?.Dispose();
                _systemAdapter?.Dispose();
                engine.Dispose();
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
        }

        base.OnFrameworkInitializationCompleted();
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

        var itemAbort = new NativeMenuItem("⛔ Abortar Flujo Activo");
        itemAbort.Click += (s, e) => _mainViewModel?.AbortTask();

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
        menu.Items.Add(itemAbort);
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
                    if (_trayIcon != null)
                    {
                        _trayIcon.Icon = DynamicTrayIconRenderer.RenderTrayIcon(state, progress, secondsRemaining, pulsePhase);
                        _trayIcon.ToolTipText = state switch
                        {
                            TrayIconVisualState.InProgress => $"Nokto: {progress:F0}% ({secondsRemaining}s)",
                            TrayIconVisualState.Completed => "Nokto: Tarea completada con éxito",
                            _ => "Nokto — Sistema de Energía (Listo)"
                        };
                    }
                });
            };
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        }
    }
}
