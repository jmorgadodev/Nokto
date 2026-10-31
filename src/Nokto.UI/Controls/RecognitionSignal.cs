using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Nokto.UI.Controls;

/// <summary>Onda de nivel real al escuchar; barrido de búsqueda al consultar. Sin reloj en reposo.</summary>
public sealed class RecognitionSignal : Control, IDisposable
{
    public static readonly StyledProperty<string> PhaseProperty = AvaloniaProperty.Register<RecognitionSignal, string>(nameof(Phase), "Idle");
    public static readonly StyledProperty<bool> ReducedMotionProperty = AvaloniaProperty.Register<RecognitionSignal, bool>(nameof(ReducedMotion));
    public string Phase { get => GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public bool ReducedMotion { get => GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
    private readonly double[] _levels = new double[25];
    private float _targetLevel;
    private double _elapsed, _smoothed;
    private long _started;
    private bool _attached, _disposed;
    private static readonly IBrush Ink = Brush.Parse("#BEEBDF"), Quiet = Brush.Parse("#314E59"), Coral = Brush.Parse("#EAA5AC");
    public RecognitionSignal()
    {
        Height = 88; ClipToBounds = true;
        _frames.Tick += (_, _) => SampleFrame(Stopwatch.GetElapsedTime(_started).TotalSeconds);
        AttachedToVisualTree += (_, _) => { _attached = true; SyncFrames(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; _frames.Stop(); };
    }
    // Se llama desde WASAPI; no publica buffers ni eventos de UI por bloque de audio.
    public void SetLevel(float value) => Volatile.Write(ref _targetLevel, float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0);
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PhaseProperty)
        {
            _started = Stopwatch.GetTimestamp(); _elapsed = _smoothed = 0; Array.Clear(_levels); SetLevel(0);
            SyncFrames(); InvalidateVisual();
        }
        if (change.Property == ReducedMotionProperty || change.Property == IsVisibleProperty) { SyncFrames(); InvalidateVisual(); }
    }
    private void SyncFrames()
    {
        if (!_disposed && _attached && IsEffectivelyVisible && !ReducedMotion && Phase is "Listening" or "Searching") _frames.Start();
        else _frames.Stop();
    }
    private void SampleFrame(double elapsed)
    {
        if (_disposed || !IsEffectivelyVisible) { _frames.Stop(); return; }
        _elapsed = elapsed;
        double level = Math.Clamp(Math.Sqrt(Volatile.Read(ref _targetLevel)) * 1.6, 0, 1);
        _smoothed += (level - _smoothed) * (level > _smoothed ? .5 : .18);
        Array.Copy(_levels, 1, _levels, 0, _levels.Length - 1); _levels[^1] = _smoothed;
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width, middle = Bounds.Height / 2 - 5;
        if (width <= 0) return;
        bool listening = Phase == "Listening", searching = Phase == "Searching";
        if (listening || searching)
        {
            double step = Math.Min(8, (width - 32) / _levels.Length), left = (width - step * (_levels.Length - 1)) / 2;
            for (int i = 0; i < _levels.Length; i++)
            {
                double value = ReducedMotion ? .22 : listening ? _levels[i] :
                    .16 + .56 * Math.Exp(-Math.Pow((i - ((_elapsed * 12 + 12) % 36 - 6)) / 4, 2));
                double height = 3 + 44 * value;
                context.DrawRectangle(i < 6 && listening ? Quiet : Ink, null, new Rect(left + i * step - 2, middle - height / 2, 4, height), 2, 2);
            }
            if (listening)
            {
                var track = new Rect(16, Bounds.Height - 8, Math.Max(1, width - 32), 2);
                context.DrawRectangle(Quiet, null, track, 1, 1);
                if (!ReducedMotion) context.DrawRectangle(Ink, null, track.WithWidth(track.Width * Math.Clamp(_elapsed / 8, 0, 1)), 1, 1);
            }
            return;
        }
        bool success = Phase == "Success", error = Phase == "Error";
        var pen = new Pen(error ? Coral : Ink, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double x = width / 2;
        if (success)
        {
            context.DrawLine(pen, new Point(x - 12, middle), new Point(x - 3, middle + 9));
            context.DrawLine(pen, new Point(x - 3, middle + 9), new Point(x + 15, middle - 11));
        }
        else if (error)
        {
            context.DrawLine(pen, new Point(x, middle - 13), new Point(x, middle + 3));
            context.DrawEllipse(Coral, null, new Point(x, middle + 12), 2, 2);
        }
        else
        {
            context.DrawLine(pen, new Point(x + 1, middle + 8), new Point(x + 1, middle - 13));
            context.DrawLine(pen, new Point(x + 1, middle - 13), new Point(x + 13, middle - 16));
            context.DrawLine(pen, new Point(x + 13, middle - 16), new Point(x + 13, middle + 4));
            context.DrawEllipse(Ink, null, new Point(x - 4, middle + 9), 5, 4);
            context.DrawEllipse(Ink, null, new Point(x + 8, middle + 5), 5, 4);
        }
    }
    public void Dispose() { _disposed = true; _frames.Stop(); SetLevel(0); Array.Clear(_levels); }
}
