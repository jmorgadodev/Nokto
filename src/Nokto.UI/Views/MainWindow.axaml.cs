using Avalonia.Controls;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class MainWindow : Window
{
    private bool _isRealShutdown;
    private GraceOverlayWindow? _graceOverlay;
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnMainWindowClosing;
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
            qrModal.ShowDialog(this);
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isRealShutdown)
        {
            // Minimiza a la bandeja del sistema en vez de cerrarse
            e.Cancel = true;
            Hide();
        }
    }

    public void ForceCloseApplication()
    {
        _isRealShutdown = true;
        _graceOverlay?.Close();
        Close();
    }
}
