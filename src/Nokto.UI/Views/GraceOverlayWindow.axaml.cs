using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class GraceOverlayWindow : Window
{
    private MainViewModel? _viewModel;

    public GraceOverlayWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }

    public void AttachViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void UpdateCountdown(int secondsRemaining)
    {
        if (secondsRemaining > ProgressBarGrace.Maximum)
        {
            ProgressBarGrace.Maximum = secondsRemaining;
        }
        TxtCountdown.Text = $"Aviso: {secondsRemaining} s";
        ToolTip.SetTip(TxtCountdown, _viewModel?.GraceRoutineName);
        ProgressBarGrace.Value = Math.Max(0, secondsRemaining);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RepositionToTopRight();
    }

    private void RepositionToTopRight()
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            int x = workingArea.X + workingArea.Width - (int)(Width * screen.Scaling) - 20;
            int y = workingArea.Y + 30;
            Position = new PixelPoint(x, y);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel?.FinishGraceRoutine();
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            _viewModel?.PostponeTask("10");
            Hide();
            e.Handled = true;
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel?.FinishGraceRoutine();
        Hide();
    }

    private void OnPostponeClicked(object? sender, RoutedEventArgs e)
    {
        _viewModel?.PostponeTask("10");
        Hide();
    }
}
