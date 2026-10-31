using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Nokto.UI.Controls;

/// <summary>Control de audio con geometría fija; los estados cambian tinta e icono, nunca tamaño.</summary>
public sealed class IslandAudioButton : Button
{
    public static readonly StyledProperty<bool> IsMutedProperty = AvaloniaProperty.Register<IslandAudioButton, bool>(nameof(IsMuted));
    public static readonly StyledProperty<bool> IsMicrophoneProperty = AvaloniaProperty.Register<IslandAudioButton, bool>(nameof(IsMicrophone));
    public static readonly StyledProperty<bool> ReducedMotionProperty = AvaloniaProperty.Register<IslandAudioButton, bool>(nameof(ReducedMotion));
    public bool IsMuted { get => GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }
    public bool IsMicrophone { get => GetValue(IsMicrophoneProperty); set => SetValue(IsMicrophoneProperty, value); }
    public bool ReducedMotion { get => GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    private Border? _disc;
    private ShapePath? _icon, _waves, _slash;

    public IslandAudioButton()
    {
        Width = Height = 48; Padding = new Thickness(0); HorizontalAlignment = HorizontalAlignment.Center;
        Template = new FuncControlTemplate<Button>((_, _) =>
        {
            var glyph = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _icon = Stroke(); _waves = Stroke(); _slash = Stroke();
            _slash.Data = Geometry.Parse("M 3,3 L 21,21");
            glyph.Children.Add(_icon); glyph.Children.Add(_waves); glyph.Children.Add(_slash);
            _disc = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), Child = glyph };
            Refresh(); return _disc;
        });
    }
    private static ShapePath Stroke() => new() { StrokeThickness = 1.65, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsMutedProperty || change.Property == IsMicrophoneProperty || change.Property == ReducedMotionProperty ||
            change.Property == IsPointerOverProperty || change.Property == IsPressedProperty || change.Property == IsEnabledProperty || change.Property == IsEffectivelyEnabledProperty || change.Property == IsKeyboardFocusWithinProperty) Refresh();
    }
    private void Refresh()
    {
        if (_disc is null || _icon is null || _waves is null || _slash is null) return;
        if (ReducedMotion) _disc.Transitions = null;
        else _disc.Transitions ??= new Transitions
        {
            new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(150) },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(150) }
        };
        _disc.Background = Brush.Parse(IsMuted ? (IsPressed ? "#60343F" : IsPointerOver ? "#51303C" : "#3B2731") :
            IsPressed ? "#355C5C" : IsPointerOver ? "#2C4D50" : "#203B40");
        _disc.BorderBrush = Brush.Parse(IsKeyboardFocusWithin ? "#E1FFF8" : IsMuted ? "#AC6F7C" : IsPointerOver ? "#72B2AC" : "#496D72");
        var ink = Brush.Parse(IsMuted ? "#FFBCC6" : "#CFF9ED");
        _icon.Stroke = _waves.Stroke = _slash.Stroke = ink;
        _icon.Data = Geometry.Parse(IsMicrophone
            ? "M 12,3 C 9.8,3 8.5,4.7 8.5,7 L 8.5,11 C 8.5,15.7 15.5,15.7 15.5,11 L 15.5,7 C 15.5,4.7 14.2,3 12,3 M 5.5,10.5 C 5.5,19.2 18.5,19.2 18.5,10.5 M 12,17 L 12,21 M 8.5,21 L 15.5,21"
            : "M 3,9 L 7,9 L 12,5 L 12,19 L 7,15 L 3,15 Z");
        _waves.Data = Geometry.Parse("M 16,9 C 18,10.5 18,13.5 16,15 M 19,6 C 23,9 23,15 19,18");
        _waves.IsVisible = !IsMicrophone && !IsMuted;
        _slash.IsVisible = IsMuted;
        _disc.Opacity = IsEffectivelyEnabled ? 1 : .4;
        AutomationProperties.SetName(this, IsMicrophone ? (IsMuted ? "Activar micrófono" : "Silenciar micrófono") : (IsMuted ? "Activar salida de audio" : "Silenciar salida de audio"));
    }
}
