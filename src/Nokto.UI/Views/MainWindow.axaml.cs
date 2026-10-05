using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class MainWindow : Window
{
    private bool _isExplicitExit;
    private GraceOverlayWindow? _graceOverlay;
    private MainViewModel? _viewModel;
    internal bool IsExiting => _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();
        WindowState = Avalonia.Controls.WindowState.Maximized;
        Closing += OnMainWindowClosing;

        try
        {
            Icon = Nokto.UI.Tray.DynamicTrayIconRenderer.RenderAppWindowIcon();
        }
        catch
        {
            // Fallback ante entornos sin aceleración gráfica inicial
        }
    }

    public void InitializeWithViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.RequestGraceOverlay += HandleRequestGraceOverlay;
        _viewModel.RequestFilePicker += async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Seleccionar aplicación o script",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Ejecutables y Scripts (*.exe, *.bat, *.cmd, *.ps1)")
                        {
                            Patterns = new[] { "*.exe", "*.bat", "*.cmd", "*.ps1" }
                        }
                    }
                });
                if (files.Count > 0)
                {
                    return files[0].Path.LocalPath;
                }
            }
            return null;
        };
    }

    private void HandleRequestGraceOverlay(bool show, int secondsRemaining)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_isExplicitExit || PlatformImpl is null) return;
            if (show)
            {
                if (_graceOverlay == null)
                {
                    _graceOverlay = new GraceOverlayWindow();
                    if (_viewModel != null)
                    {
                        _graceOverlay.AttachViewModel(_viewModel);
                    }
                }
                _graceOverlay.UpdateCountdown(secondsRemaining);
                if (!_graceOverlay.IsVisible)
                {
                    _graceOverlay.Show();
                }
            }
            else
            {
                _graceOverlay?.Hide();
            }
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isExplicitExit)
        {
            bool minimizeToTray = _viewModel?.MinimizeToTrayOnClose ?? true;
            if (minimizeToTray)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _isExplicitExit = true;
                _graceOverlay?.Close();
                _graceOverlay = null;
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && WindowState == WindowState.Minimized)
        {
            // Al minimizar [-], oculta de la barra de tareas hacia el System Tray
            Hide();
        }
    }

    public void ForceCloseApplication()
    {
        _isExplicitExit = true;
        _graceOverlay?.Close();
        _graceOverlay = null;
        Close();
    }

    private void OnExitNoktoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel?.FinishTask();
        ForceCloseApplication();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void OnOpenLinkedInClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.linkedin.com/in/jorge-morgado/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Error opening LinkedIn: {ex.Message}");
        }
    }

    private void OnOpenGitHubClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/jmorgadodev/Nokto",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Error opening GitHub: {ex.Message}");
        }
    }
}
