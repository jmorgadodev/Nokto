using Avalonia.Interactivity;
using Nokto.Core.Models;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private IslandProductivityViewModel? Productivity => _settings?.Productivity;
    private bool _productivityOpen;

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
        // Una alarma visible no desaparece por salida accidental del cursor.
        _pinned = true;
        SetExpanded(true);
        RefreshProductivity();
    }

    private void RefreshProductivity()
    {
        if (Productivity is not { } p) return;
        bool enabled = p.Enabled;
        ProductivityPanel.IsVisible = enabled;
        ProductivityDetails.IsVisible = enabled && _productivityOpen;
        ProductivityAlertFrame.IsVisible = enabled && p.HasAlert;
        ProductivityAlertText.Text = p.AlertText;
        CompactProductivityText.Text = p.CompactText;
        CompactProductivityText.IsVisible = !(_expanded || !p.HasCompactText);
        if (!_islandReady || _animationTimer is null) return;

        double targetWidth = _expanded ? ExpandedWidth : CompactWidth;
        double targetHeight = _expanded ? ExpandedHeightForContent : CompactHeight;
        if (Math.Abs(targetWidth - _toWidth) > .5 || Math.Abs(targetHeight - _toHeight) > .5)
            AnimateTo(targetWidth, targetHeight);
    }

    private void OnToggleProductivity(object? sender, RoutedEventArgs e)
    {
        _productivityOpen = !_productivityOpen;
        _pinned = true;
        if (!_expanded) SetExpanded(true);
        RefreshProductivity();
    }
    private void OnDismissProductivityAlert(object? sender, RoutedEventArgs e)
        => Productivity?.DismissAlert();

    private void OnTimer5(object? sender, RoutedEventArgs e) => Productivity?.StartTimer(5);
    private void OnTimer15(object? sender, RoutedEventArgs e) => Productivity?.StartTimer(15);
    private void OnTimer25(object? sender, RoutedEventArgs e) => Productivity?.StartTimer(25);
    private void OnTimerToggle(object? sender, RoutedEventArgs e) => Productivity?.ToggleTimer();
    private void OnTimerReset(object? sender, RoutedEventArgs e) => Productivity?.ResetTimer();
    private void OnStopwatchToggle(object? sender, RoutedEventArgs e) => Productivity?.ToggleStopwatch();
    private void OnStopwatchReset(object? sender, RoutedEventArgs e) => Productivity?.ResetStopwatch();
    private void OnPomodoroToggle(object? sender, RoutedEventArgs e) => Productivity?.TogglePomodoro();
    private void OnPomodoroReset(object? sender, RoutedEventArgs e) => Productivity?.ResetPomodoro();

    private void OnAddAlarm(object? sender, RoutedEventArgs e)
    {
        bool ok = Productivity?.AddAlarm(AlarmTimeInput.Text?.Trim() ?? "", AlarmDailyCheck.IsChecked == true) == true;
        ProductivityValidationText.IsVisible = !ok;
        ProductivityValidationText.Text = ok ? "" : "Alarma: usa HH:mm entre 00:00 y 23:59 (máximo 12).";
        if (ok) AlarmTimeInput.Text = "";
    }
    private void OnDeleteAlarm(object? sender, RoutedEventArgs e) => Productivity?.RemoveNextAlarm();

    private void OnAddTask(object? sender, RoutedEventArgs e)
    {
        int delay = TaskReminderChoice.SelectedIndex switch { 1 => 15, 2 => 60, _ => 0 };
        bool ok = Productivity?.AddTask(TaskInput.Text ?? "", delay) == true;
        ProductivityValidationText.IsVisible = !ok;
        ProductivityValidationText.Text = ok ? "" : "Tarea: escribe 1–120 caracteres (máximo 30).";
        if (ok) TaskInput.Text = "";
    }
    private void OnCompleteTask(object? sender, RoutedEventArgs e) => Productivity?.CompleteNextTask();
}
