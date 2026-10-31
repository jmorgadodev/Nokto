using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nokto.Core.Services;
using Nokto.UI.Controls;
using System.Diagnostics;

namespace Nokto.UI.Views;

public sealed class IslandLyricsWindow : Window
{
    private readonly TextBlock _status = new() { Foreground = Brush.Parse("#A6BAC8"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _track = new() { Foreground = Brush.Parse("#D6E8EF"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 270 };
    private readonly StackPanel _lines = new() { Spacing = 7 };
    private readonly IslandButton _retry = new() { Content = "Reintentar", Appearance = "Secondary", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _surface;
    private readonly StackPanel _root;
    private IReadOnlyList<LyricLine> _lyrics = [];
    private int _active = int.MinValue;
    private bool _canSeek;
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private long _resizeStart;
    private double _fromHeight, _targetHeight;
    private readonly bool _reducedMotion;
    private readonly DispatcherTimer _lineTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly TranslateTransform _lineTranslation = new();
    private long _lineStart;
    public event Action<TimeSpan>? SeekRequested;
    public event Action? DismissRequested;
    public event Action? RetryRequested;

    public IslandLyricsWindow(bool reducedMotion = false)
    {
        _reducedMotion = reducedMotion;
        Width = 340; Height = 220; CanResize = false; SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; WindowStartupLocation = WindowStartupLocation.Manual;
        var root = _root = new StackPanel { Spacing = 10 };
        var heading = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        heading.Children.Add(new TextBlock { Text = "LETRAS", FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#8FD7C9"), VerticalAlignment = VerticalAlignment.Center });
        var close = new IslandButton { Content = "×", Appearance = "Quiet", Width = 28, Height = 28, MinHeight = 0, Padding = new Thickness(0), ReducedMotion = reducedMotion };
        Avalonia.Automation.AutomationProperties.SetName(close, "Cerrar letras");
        Grid.SetColumn(close, 1); heading.Children.Add(close); close.Click += (_, _) => DismissRequested?.Invoke();
        root.Children.Add(heading); root.Children.Add(_track); root.Children.Add(_status); root.Children.Add(_lines);
        _retry.Click += (_, _) => RetryRequested?.Invoke(); root.Children.Add(_retry);
        root.Children.Add(new TextBlock { Text = "Letras · LRCLIB", FontSize = 10, Foreground = Brush.Parse("#91AAB6") });
        _surface = new Border { Child = root, CornerRadius = new CornerRadius(22), Padding = new Thickness(16),
            Background = Brush.Parse("#10151E"), BorderBrush = Brush.Parse("#24353E"), BorderThickness = new Thickness(1) };
        Content = _surface;
        Opened += (_, _) => Dispatcher.UIThread.Post(FitHeight, DispatcherPriority.Loaded);
        _lines.RenderTransform = _lineTranslation;
        _lineTimer.Tick += (_, _) =>
        {
            double progress = Math.Clamp(Stopwatch.GetElapsedTime(_lineStart).TotalSeconds / .22, 0, 1);
            double eased = 1 - Math.Pow(1-progress, 3);
            _lines.Opacity = .65 + .35 * eased; _lineTranslation.Y = 5 * (1-eased);
            if (progress >= 1) _lineTimer.Stop();
        };
        _sizeTimer.Tick += (_, _) =>
        {
            double elapsed = Stopwatch.GetElapsedTime(_resizeStart).TotalSeconds;
            Height = IslandSpring.Sample(_fromHeight, _targetHeight, 0, elapsed).Position;
            if (elapsed >= .7) { Height = _targetHeight; _sizeTimer.Stop(); }
        };
        Closed += (_, _) => { _sizeTimer.Stop(); _lineTimer.Stop(); };
    }
    public void Loading(string title)
    {
        _retry.IsVisible = false;
        _track.Text = title; _status.Text = "Buscando letra sincronizada…";
        _lyrics = []; _lines.Children.Clear(); _active = int.MinValue; FitHeight();
    }
    public void SetLyrics(SongLyrics lyrics, bool canSeek)
    {
        _retry.IsVisible = false;
        _lyrics = lyrics.Lines; _canSeek = canSeek;
        _status.Text = lyrics.Instrumental ? "Pista instrumental" : _lyrics.Count > 0
            ? canSeek ? "Pulsa una línea para ir a ese momento" : "Este reproductor no permite saltar de posición"
            : string.IsNullOrWhiteSpace(lyrics.PlainText) ? "No hay letra disponible para esta pista" : "Vista previa de letra sin sincronización";
        _lines.Children.Clear(); _active = int.MinValue;
        if (_lyrics.Count == 0 && !string.IsNullOrWhiteSpace(lyrics.PlainText))
            _lines.Children.Add(new TextBlock { Text = string.Join("\n", lyrics.PlainText.Split('\n').Take(4).Select(line => line.Length > 300 ? line[..300] + "…" : line)), TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#B9CBD4"), MaxHeight = 120 });
        UpdatePosition(TimeSpan.Zero); FitHeight();
    }
    public void Error(string message) { _status.Text = message; _lines.Children.Clear(); _lyrics = []; _retry.IsVisible = true; FitHeight(); }
    public void PlaybackNotice(string message) => _status.Text = message;
    public void UpdatePosition(TimeSpan position, bool? canSeek = null)
    {
        if (canSeek.HasValue && canSeek.Value != _canSeek) { _canSeek = canSeek.Value; _active = int.MinValue; }
        if (_lyrics.Count == 0) return;
        int active = IslandLyricsService.ActiveLine(_lyrics, position);
        if (_active == active) return;
        _active = active;
        _lines.Children.Clear();
        int center = Math.Max(0, active);
        for (int i = Math.Max(0, center - 1); i <= Math.Min(_lyrics.Count - 1, center + 2); i++)
        {
            var line = _lyrics[i]; bool current = i == active;
            string text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;
            var label = new TextBlock { Text = text.Length > 300 ? text[..300] + "…" : text, TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse(current ? "#E2FFF5" : "#839DAA"), FontSize = current ? 15 : 12, FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal };
            var button = new IslandButton { Content = label, IsEnabled = _canSeek, DisabledOpacity = 1, ReducedMotion = _reducedMotion, Appearance = current ? "Selected" : "Quiet", MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(9, 7), CornerRadius = new CornerRadius(10),
                Background = current ? Brush.Parse("#213D42") : Brushes.Transparent,
                Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } } };
            button.Click += (_, _) => SeekRequested?.Invoke(line.Time);
            _lines.Children.Add(button);
        }
        if (!_reducedMotion) { _lineStart = Stopwatch.GetTimestamp(); _lines.Opacity = .65; _lineTranslation.Y = 5; _lineTimer.Start(); }
        FitHeight();
    }
    private void FitHeight()
    {
        // Medir el contenido natural evita realimentar la altura de la ventana animada.
        _root.InvalidateMeasure(); _root.Measure(new Size(Math.Max(160, Width - 34), double.PositiveInfinity));
        double desired = Math.Clamp(_root.DesiredSize.Height + 34, 150, 440);
        if (_reducedMotion) { Height = desired; _targetHeight = desired; return; }
        if (Math.Abs(desired - _targetHeight) < 2) return;
        _fromHeight = Height; _targetHeight = desired; _resizeStart = Stopwatch.GetTimestamp(); _sizeTimer.Start();
    }
}
