using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nokto.UI.Controls;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private readonly Dictionary<string, Control> _sections = new();
    private readonly Grid _contentGrid = new();
    private LayoutTransformControl? _contentScaleHost;
    private double _contentWidth = 308;
    private double _contentHeight;
    private string _layoutPlan = "";
    private readonly Grid _audioPair = new() { ColumnDefinitions = ColumnDefinitions.Parse("*,1,*") };

    private static T Take<T>(T control) where T : Control
    {
        if (control.Parent is Panel panel) panel.Children.Remove(control);
        else if (control.Parent is Decorator decorator) decorator.Child = null;
        else if (control.Parent is ContentControl content) content.Content = null;
        return control;
    }

    private static Border SectionCard(Control child) => new()
    {
        Child = child, Background = Brush.Parse("#17212C"),
        BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(14), Padding = new Thickness(14)
    };

    private void InitializeContentSections()
    {
        var media = new StackPanel { Spacing = 12 };
        MediaPanel.Background = MediaEmptyState.Background = Brushes.Transparent;
        MediaPanel.BorderThickness = MediaEmptyState.BorderThickness = new Thickness(0);
        MediaPanel.Padding = MediaEmptyState.Padding = new Thickness(0);
        MediaToolsRow.Margin = new Thickness(0);
        media.Children.Add(Take(MediaPanel)); media.Children.Add(Take(MediaEmptyState));
        media.Children.Add(Take(MediaToolsRow));
        _sections["media"] = SectionCard(media);
        WeatherPanel.Background = Brush.Parse("#17212C");
        WeatherPanel.BorderThickness = new Thickness(0); WeatherPanel.CornerRadius = new CornerRadius(14); WeatherPanel.Padding = new Thickness(14);
        _sections["weather"] = Take(WeatherPanel);
        BatteryStatusText.HorizontalAlignment = HorizontalAlignment.Center;
        BatteryStatusText.TextAlignment = TextAlignment.Center;
        BatteryStatusText.FontSize = 12;
        BatteryStatusText.Foreground = Brush.Parse("#D3EEE5");
        BatteryStatusText.TextWrapping = TextWrapping.Wrap;
        var power = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        power.Children.Add(new Avalonia.Controls.Shapes.Path { Width = 15, Height = 18, Stretch = Stretch.Uniform,
            Fill = Brush.Parse("#8ADAB6"), Data = Geometry.Parse("M 9,0 L 1,10 L 7,10 L 5,18 L 14,6 L 8,6 Z") });
        power.Children.Add(Take(BatteryStatusText));
        _sections["battery"] = SectionCard(power);
        NetworkStatusText.TextAlignment = TextAlignment.Center;
        NetworkStatusText.TextWrapping = TextWrapping.Wrap;
        NetworkStatusText.Foreground = Brush.Parse("#BCD1DA");
        var network = new StackPanel { Spacing = 7 };
        network.Children.Add(new TextBlock { Text = "Conexión", FontSize = 12, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, Foreground = Brush.Parse("#E1EFEF") });
        network.Children.Add(Take(NetworkStatusText));
        _sections["network"] = SectionCard(network);
        var volume = new StackPanel { Spacing = 8 };
        var volumeHeading = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        volumeHeading.Children.Add(new TextBlock { Text = "Volumen del sistema", Foreground = Brush.Parse("#D8EAE9"), FontSize = 12, FontWeight = FontWeight.SemiBold });
        VolumeValueText.FontFamily = FontFamily.Default; VolumeValueText.FontSize = 13;
        volumeHeading.Children.Add(Take(VolumeValueText));
        volume.Children.Add(volumeHeading); volume.Children.Add(Take(VolumeRow));
        _sections["volume"] = SectionCard(volume);
        MicControl.Orientation = OutputControl.Orientation = Orientation.Vertical;
        foreach (var label in MicControl.Children.Concat(OutputControl.Children).OfType<TextBlock>())
        {
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextAlignment = TextAlignment.Center;
            label.TextWrapping = TextWrapping.Wrap;
            label.MaxWidth = 126;
        }
        _sections["microphone"] = SectionCard(Take(MicControl));
        _sections["output"] = SectionCard(Take(OutputControl));
        _sections["audioPair"] = SectionCard(_audioPair);
        var divider = new Border { Width = 1, Height = 54, Background = Brush.Parse("#30424E"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(divider, 1); _audioPair.Children.Add(divider);
        _sections["routines"] = TitledSection("Rutinas", Take(ProductivityPanel));
        ExplorerButton.Content = "Abrir Explorador";
        ExplorerButton.Background = Brush.Parse("#1A2D3B");
        ExplorerButton.Foreground = Brush.Parse("#E4EEF5");
        ExplorerButton.BorderBrush = Brush.Parse("#304959");
        ExplorerButton.BorderThickness = new Thickness(1);
        ExplorerButton.CornerRadius = new CornerRadius(12);
        ExplorerButton.Padding = new Thickness(12, 10);
        ExplorerButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        ExplorerButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _sections["explorer"] = Take(ExplorerButton);
        var folders = new StackPanel { Spacing = 10 };
        var heading = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        heading.Children.Add(new TextBlock { Text = "Mis carpetas", FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#E4EEF5"), VerticalAlignment = VerticalAlignment.Center });
        Take(AddFolderButton); Grid.SetColumn(AddFolderButton, 1); heading.Children.Add(AddFolderButton);
        AddFolderButton.Content = "+ Añadir";
        AddFolderButton.Background = Brush.Parse("#273C4B");
        AddFolderButton.Foreground = Brush.Parse("#D6F3F5");
        AddFolderButton.BorderThickness = new Thickness(0);
        AddFolderButton.CornerRadius = new CornerRadius(8);
        folders.Children.Add(heading); folders.Children.Add(Take(FolderCountLabel));
        folders.Children.Add(Take(QuickFolderList)); folders.Children.Add(Take(QuickFolderStatus));
        QuickFolderStatus.IsVisible = !string.IsNullOrWhiteSpace(QuickFolderStatus.Text);
        QuickFolderStatus.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBlock.TextProperty) return;
            QuickFolderStatus.IsVisible = !string.IsNullOrWhiteSpace(QuickFolderStatus.Text);
            if (_islandReady && _expanded) AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        };
        _sections["folders"] = SectionCard(folders);
        _sections["files"] = TitledSection("Archivos", Take(FileShelfPanel));
        _sections["clipboard"] = TitledSection("Portapapeles", Take(ClipboardPanel));
        _sections["codex"] = SectionCard(CreateCodexUsagePanel());
        _contentScaleHost = new LayoutTransformControl { Child = _contentGrid };
        PageScrollViewer.Content = _contentScaleHost;
    }

    private static Border TitledSection(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#E1EFEF") });
        stack.Children.Add(content); return SectionCard(stack);
    }

    private void ArrangeContentSections()
    {
        if (_settings is null || _contentScaleHost is null) return;
        double screenHeight = Screens.Primary is { } screen ? screen.WorkingArea.Height / screen.Scaling / ScaleFactor : 900;
        double screenWidth = Screens.Primary is { } display ? display.WorkingArea.Width / display.Scaling / ScaleFactor : 1400;
        bool navigation = _recognitionView is null && _settings.AvailablePages.Count > 1;
        double availableHeight = Math.Max(160, screenHeight - (IslandFooter.IsVisible ? 130 : 96) - (navigation ? 40 : 0));
        double availableWidth = Math.Max(220, screenWidth - 32 - (_lyricsOpen ? Math.Min(352, screenWidth * .4) : 0));
        IslandPageNav.IsVisible = navigation;
        if (_recognitionView is { } recognition)
        {
            _contentScaleHost.Child = recognition;
            recognition.Width = 308;
            recognition.InvalidateMeasure(); recognition.Measure(new Size(308, double.PositiveInfinity));
            FitContent(308, recognition.DesiredSize.Height, availableWidth, availableHeight);
            return;
        }
        _contentScaleHost.Child = _contentGrid;
        bool Enabled(string id) => _settings.FeatureInSlot(id, _settings.PageSlotNumber(_pageIndex) - 1) &&
            (!_settings.PrivacyMode || id is not ("folders" or "files" or "clipboard" or "weather" or "battery" or "network" or "codex"));
        bool paired = Enabled("microphone") && Enabled("output");
        if (paired && MicControl.Parent != _audioPair)
        {
            Take(MicControl); Take(OutputControl);
            Grid.SetColumn(MicControl, 0); Grid.SetColumn(OutputControl, 2);
            _audioPair.Children.Add(MicControl); _audioPair.Children.Add(OutputControl);
        }
        else if (!paired && MicControl.Parent == _audioPair)
        {
            Take(MicControl); Take(OutputControl);
            ((Border)_sections["microphone"]).Child = MicControl;
            ((Border)_sections["output"]).Child = OutputControl;
        }
        var active = _settings.ContentOrderForPage(_pageIndex).Where(Enabled)
            .Select(id => paired && id is "microphone" or "output" ? "audioPair" : id).Distinct().ToArray();
        var heights = new Dictionary<string, double>();
        foreach (string id in active)
        {
            var control = _sections[id];
            control.InvalidateMeasure();
            control.Measure(new Size(308, double.PositiveInfinity));
            heights[id] = Math.Max(1, control.DesiredSize.Height);
        }
        double sum = active.Sum(id => heights[id]) + 8 * Math.Max(0, active.Length - 1);
        int maxColumns = Math.Max(1, Math.Min(active.Length, (int)(availableWidth / 324)));
        int columns = Math.Clamp((int)Math.Ceiling(sum / availableHeight), 1, maxColumns);
        var positions = IslandColumnLayout.Place(active.Select(id => heights[id]).ToArray(), columns);
        var placements = active.Select((id, i) => (Id: id, Column: positions[i])).ToList();
        string plan = columns + ":" + string.Join(";", placements.Select(x => x.Id + ":" + x.Column));
        if (plan != _layoutPlan)
        {
            _layoutPlan = plan;
            foreach (var section in _sections.Values) Take(section);
            _contentGrid.Children.Clear(); _contentGrid.ColumnDefinitions.Clear(); _contentGrid.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) _contentGrid.ColumnDefinitions.Add(new ColumnDefinition(308 + (i < columns - 1 ? 12 : 0), GridUnitType.Pixel));
            var stacks = Enumerable.Range(0, columns).Select(_ => new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top }).ToArray();
            foreach (var placement in placements) stacks[placement.Column].Children.Add(_sections[placement.Id]);
            for (int i = 0; i < columns; i++)
            {
                Grid.SetColumn(stacks[i], i); stacks[i].Margin = new Thickness(0, 0, i < columns-1 ? 12 : 0, 0);
                _contentGrid.Children.Add(stacks[i]);
            }
        }
        double naturalWidth = columns * 320 - 12;
        _contentGrid.Width = naturalWidth;
        _contentGrid.InvalidateMeasure(); _contentGrid.Measure(new Size(naturalWidth, double.PositiveInfinity));
        double naturalHeight = _contentGrid.DesiredSize.Height;
        FitContent(naturalWidth, naturalHeight, availableWidth, availableHeight);
    }

    private void FitContent(double naturalWidth, double naturalHeight, double availableWidth, double availableHeight)
    {
        if (_contentScaleHost is null) return;
        double scale = Math.Min(1, Math.Min(availableWidth / naturalWidth, availableHeight / Math.Max(1, naturalHeight)));
        _contentScaleHost.LayoutTransform = new ScaleTransform(scale, scale);
        _contentWidth = naturalWidth * scale;
        _contentHeight = naturalHeight * scale;
        PageScrollViewer.Height = _contentHeight;
        PageScrollViewer.Width = _contentWidth;
        ExpandedTools.Width = _contentWidth;
        PageScrollViewer.Offset = default;
    }
}
