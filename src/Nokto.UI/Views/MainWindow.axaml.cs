using Avalonia;
using Avalonia.Controls;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class MainWindow : Window
{
    private bool _isExplicitExit;
    private GraceOverlayWindow? _graceOverlay;
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
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
        _viewModel.RequestQrModal += HandleRequestQrModal;
    }

    private void HandleRequestGraceOverlay(bool show, int secondsRemaining)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
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

    private void HandleRequestQrModal()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var qrModal = new QrModalWindow();
            qrModal.SetConnectionDetails(_viewModel?.LanConnectionUrl ?? "http://localhost:4884");
            qrModal.Closed += (s, e) =>
            {
                _viewModel?.StopLanServer();
            };
            qrModal.ShowDialog(this);
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isExplicitExit)
        {
            // Residencia en segundo plano: oculta la ventana a la bandeja del sistema
            e.Cancel = true;
            Hide();
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
        Close();
    }
}
