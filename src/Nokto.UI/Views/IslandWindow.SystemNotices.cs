using Avalonia.Threading;
using Nokto.Platform.Windows.Notifications;
using Nokto.Core.Models;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private IslandSystemObserver? _systemObserver;
    private DispatcherTimer? _noticeTimer;
    private bool _noticeOnly;
    private HashSet<string> _observedNotices = [];

    private void SyncSystemObserver()
    {
        var choices = _settings?.EnabledNoticeIds() ?? [];
        bool enabled = _islandReady && choices.Count > 0;
        if (!enabled) { DisposeSystemNotices(); return; }
        if (_systemObserver is not null && choices.SetEquals(_observedNotices)) return;
        if (_systemObserver is { } previous) { previous.Notice -= OnSystemNotice; previous.Dispose(); }
        _observedNotices = choices;
        _systemObserver = new IslandSystemObserver(choices);
        _systemObserver.Notice += OnSystemNotice;
        // WMI y WASAPI se inicializan fuera de la transición de apertura.
        var observer = _systemObserver;
        _ = Task.Run(() => observer.Start());
    }

    private void OnSystemNotice(IslandSystemNotice notice) => Dispatcher.UIThread.Post(() => ShowSystemNotice(notice));
    private void ShowSystemNotice(IslandSystemNotice notice)
    {
        if (!_islandReady || _settings is null) return;
        // El menú ya muestra los estados: ningún aviso debe añadir filas o mover sus controles.
        if ((_expanded && !_noticeOnly) || DateTimeOffset.UtcNow < _menuFeedbackUntil) return;
        // WASAPI también devuelve eventos de nuestros propios botones/deslizador, a veces con retraso.
        if (notice.Kind is "volume" or "microphone" && DateTimeOffset.UtcNow < _audioFeedbackUntil) return;
        if (!_settings.IsNoticeEnabled(notice.Kind)) return;
        if (_settings.PrivacyMode && IslandNoticeCatalog.IsSensitive(notice.Kind)) return;
        HudTitle.Text = notice.Title;
        HudDetail.Text = notice.Detail;
        HudValue.Text = notice.Level.HasValue ? $"{notice.Level:0}%" : "";
        HudLevel.IsVisible = notice.Level.HasValue;
        HudLevel.Value = Math.Clamp(notice.Level ?? 0, 0, 100);
        HudSymbolIcon.Data = Avalonia.Media.Geometry.Parse(notice.Kind switch
        {
            "volume" => "M 2,7 L 6,7 L 11,3 L 11,17 L 6,13 L 2,13 Z M 14,7 C 16,9 16,11 14,13 M 17,4 C 21,8 21,12 17,16",
            "brightness" => "M 10,5 A 5,5 0 1 1 9.9,5 M 10,0 L 10,2 M 10,18 L 10,20 M 0,10 L 2,10 M 18,10 L 20,10 M 3,3 L 4,4 M 16,16 L 17,17 M 3,17 L 4,16 M 16,4 L 17,3",
            "capsLock" or "keys" => "M 10,2 L 2,11 L 7,11 L 7,18 L 13,18 L 13,11 L 18,11 Z",
            "numLock" or "keyboardLayout" => "M 2,4 L 18,4 L 18,16 L 2,16 Z M 5,7 L 6,7 M 9,7 L 10,7 M 13,7 L 15,7 M 5,10 L 6,10 M 9,10 L 10,10 M 13,10 L 15,10 M 6,13 L 14,13",
            "bluetooth" => "M 10,1 L 16,6 L 4,14 M 10,19 L 16,14 L 4,6 M 10,1 L 10,19",
            "power" => "M 11,1 L 3,11 L 9,11 L 7,19 L 17,7 L 11,7 Z",
            "battery" => "M 2,5 L 16,5 L 16,15 L 2,15 Z M 18,8 L 18,12 M 5,8 L 5,12 M 8,8 L 8,12 M 11,8 L 11,12",
            "energySaver" => "M 4,16 C 0,5 9,2 18,2 C 18,14 12,20 4,16 M 4,16 L 13,7",
            "audioDevice" => "M 3,13 L 3,9 A 7,7 0 0 1 17,9 L 17,13 M 3,11 L 6,11 L 6,18 L 3,18 Z M 14,11 L 17,11 L 17,18 L 14,18 Z",
            "microphone" => "M 7,5 A 3,3 0 0 1 13,5 L 13,10 A 3,3 0 0 1 7,10 Z M 4,9 L 4,10 A 6,6 0 0 0 16,10 L 16,9 M 10,16 L 10,19 M 7,19 L 13,19",
            "storage" => "M 3,3 L 17,3 L 17,17 L 3,17 Z M 3,12 L 17,12 M 12,15 L 14,15",
            _ => "M 3,7 C 7,2 13,2 17,7 M 5,10 C 8,7 12,7 15,10 M 8,13 C 9,12 11,12 12,13 M 10,16 L 10,17"
        });
        if (!_expanded) _noticeOnly = true;
        HudPanel.IsVisible = true;
        if (!_expanded) SetExpanded(true);
        else AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        _noticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _noticeTimer.Stop(); _noticeTimer.Tick -= OnNoticeExpired; _noticeTimer.Tick += OnNoticeExpired; _noticeTimer.Start();
    }
    private void OnNoticeExpired(object? sender, EventArgs args)
    {
        _noticeTimer?.Stop();
        if (_noticeOnly) SetExpanded(false);
        else
        {
            HudPanel.IsVisible = false;
            if (_expanded) AnimateTo(ExpandedWidth, ExpandedHeightForContent);
        }
    }
    private void DisposeSystemNotices()
    {
        _noticeTimer?.Stop();
        if (_systemObserver is { } observer) { observer.Notice -= OnSystemNotice; observer.Dispose(); _systemObserver = null; }
        _observedNotices = [];
        bool collapse = _noticeOnly;
        HudPanel.IsVisible = false; _noticeOnly = false;
        if (_islandReady && collapse) SetExpanded(false);
        else if (_islandReady && _expanded) AnimateTo(ExpandedWidth, ExpandedHeightForContent);
    }
}
