using Avalonia.Interactivity;
using Nokto.Core.Models;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private IslandProductivityViewModel? Productivity => _settings?.Productivity;
    private bool HasCompactProductivity => Productivity?.HasCompactText == true;

    private void SubscribeProductivity()
    {
        if (Productivity is not { } p) return;
        p.Updated += OnProductivityUpdated;
        p.AlertRaised += OnProductivityAlert;
        RefreshProductivity();
    }

    private void UnsubscribeProductivity()
    {
        if (Productivity is not { } p) return;
        p.Updated -= OnProductivityUpdated;
        p.AlertRaised -= OnProductivityAlert;
    }

    private void OnProductivityUpdated() => RefreshProductivity();

    private void OnProductivityAlert(IslandProductivityAlert alert)
    {
        if (!_islandReady || Productivity?.Enabled != true) return;
        int? routinesPage = _settings?.AvailablePages.Cast<int?>()
            .FirstOrDefault(page => page.HasValue && _settings.PageHasFeature("routines", page.Value));
        if (routinesPage.HasValue) _pageIndex = routinesPage.Value;
        _pinned = true;
        RefreshPages();
        SetExpanded(true);
        RefreshProductivity();
    }

    private void RefreshProductivity()
    {
        UpdateContextHeader();
        if (Productivity is not { } p) return;
        ProductivityPanel.IsVisible = _settings?.ShortcutsShowRoutines == true && _settings.PageHasFeature("routines", _pageIndex);
        ProductivityAlertFrame.IsVisible = p.Enabled && p.HasAlert;
        ProductivityAlertText.Text = p.AlertText;
        CompactProductivityText.Text = p.CompactText;
        CompactProductivityText.IsVisible = !_expanded && p.HasCompactText && !HasCustomCompactHeader;
        if (!_islandReady || _animationTimer is null || !_expanded) return;
        double height = ExpandedHeightForContent;
        if (Math.Abs(height - _toHeight) > .5) AnimateTo(ExpandedWidth, height);
    }

    private void OnDismissProductivityAlert(object? sender, RoutedEventArgs e)
        => Productivity?.DismissAlert();
}
