using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private DateTimeOffset _audioFeedbackUntil, _menuFeedbackUntil;

    private void RecordLocalAudioInteraction() => _audioFeedbackUntil = DateTimeOffset.UtcNow.AddSeconds(2);

    private void OnAudioInput(object? sender, RoutedEventArgs args)
    {
        if (args.Source is not Visual source) return;
        bool audio = source == VolumeRow || source == MicControl || source == OutputControl ||
            source.GetVisualAncestors().Any(v => v == VolumeRow || v == MicControl || v == OutputControl);
        if (!audio) return;
        if (args is PointerEventArgs pointer && args.RoutedEvent == InputElement.PointerMovedEvent &&
            !pointer.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        RecordLocalAudioInteraction();
    }

    private void PromoteNoticeToMenu()
    {
        _noticeTimer?.Stop(); HudPanel.IsVisible = false; _noticeOnly = false;
        ExpandedTools.IsVisible = true;
        RefreshPages(); AnimateTo(ExpandedWidth, ExpandedHeightForContent);
    }
}
