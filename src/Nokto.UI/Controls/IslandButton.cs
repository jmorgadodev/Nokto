using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nokto.UI.Controls;

/// <summary>Una sola familia visual para acciones, navegación y transporte de la isla.</summary>
public sealed class IslandButton : Button
{
    public static readonly StyledProperty<string> AppearanceProperty = AvaloniaProperty.Register<IslandButton, string>(nameof(Appearance), "Secondary");
    public static readonly StyledProperty<bool> ReducedMotionProperty = AvaloniaProperty.Register<IslandButton, bool>(nameof(ReducedMotion));
    public static readonly StyledProperty<double> DisabledOpacityProperty = AvaloniaProperty.Register<IslandButton, double>(nameof(DisabledOpacity), .36);
    public string Appearance { get => GetValue(AppearanceProperty); set => SetValue(AppearanceProperty, value); }
    public bool ReducedMotion { get => GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public double DisabledOpacity { get => GetValue(DisabledOpacityProperty); set => SetValue(DisabledOpacityProperty, value); }
    private Border? _surface;
    public IslandButton()
    {
        MinHeight = 34; Padding = new Thickness(12, 8); FontSize = 12; CornerRadius = new CornerRadius(10);
        HorizontalContentAlignment = HorizontalAlignment.Center; VerticalContentAlignment = VerticalAlignment.Center;
        this.Bind(ReducedMotionProperty, new Binding("IslandVm.ReducedMotion"));
        Template = new FuncControlTemplate<Button>((button, _) =>
        {
            var presenter = new ContentPresenter();
            presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Content)) { Source = button });
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentTemplate)) { Source = button });
            presenter.Bind(ContentPresenter.ForegroundProperty, new Binding(nameof(Foreground)) { Source = button });
            presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new Binding(nameof(HorizontalContentAlignment)) { Source = button });
            presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, new Binding(nameof(VerticalContentAlignment)) { Source = button });
            _surface = new Border { Child = presenter, BorderThickness = new Thickness(1) };
            _surface.Bind(Border.PaddingProperty, new Binding(nameof(Padding)) { Source = button });
            Refresh(); return _surface;
        });
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AppearanceProperty || change.Property == ReducedMotionProperty || change.Property == IsPointerOverProperty ||
            change.Property == IsPressedProperty || change.Property == IsEnabledProperty || change.Property == IsEffectivelyEnabledProperty ||
            change.Property == DisabledOpacityProperty || change.Property == IsKeyboardFocusWithinProperty || change.Property == CornerRadiusProperty) Refresh();
    }
    private void Refresh()
    {
        bool primary = Appearance == "Primary", quiet = Appearance == "Quiet" || Appearance == "Navigation", selected = Appearance == "Selected";
        Foreground = Brush.Parse(primary ? "#11282C" : "#D2E7E8");
        if (_surface is null) return;
        if (ReducedMotion) _surface.Transitions = null;
        else _surface.Transitions ??= new Transitions { new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(140) } };
        _surface.Background = Brush.Parse(primary ? (IsPressed ? "#ADD6CE" : IsPointerOver ? "#E3FFF5" : "#CDEEE4") :
            IsPressed ? "#355159" : IsPointerOver ? "#2A414B" : selected ? "#234247" : quiet ? "#0017212C" : "#22343F");
        _surface.BorderBrush = IsKeyboardFocusWithin ? Brush.Parse("#96D5C8") : Brushes.Transparent;
        _surface.CornerRadius = CornerRadius;
        _surface.Opacity = IsEffectivelyEnabled ? 1 : DisabledOpacity;
    }
}
