using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nokto.Core.Models;
using Nokto.Platform.Windows.Usage;
using Nokto.UI.Controls;

namespace Nokto.UI.Views;

public partial class IslandWindow
{
    private ICodexUsageReader? _codexQuotaReader;
    private CodexUsageSnapshot? _codexQuota;
    private CancellationTokenSource? _codexRequest;
    private DateTimeOffset _codexAttempt;
    private bool _quotaUsable, _headerRightUsesClock = true;
    private readonly TextBlock _codexStatus = new() { Text = "Activa Cuota de Codex en el módulo que prefieras.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#9CB8C4") };
    private readonly TextBlock _codexPrimaryTitle = new(), _codexPrimaryValue = new(), _codexPrimaryReset = new();
    private readonly TextBlock _codexWeeklyTitle = new(), _codexWeeklyValue = new(), _codexWeeklyReset = new();
    private readonly ProgressBar _codexPrimaryLevel = new(), _codexWeeklyLevel = new();
    private readonly IslandButton _codexRefresh = new() { Content = "↻", Appearance = "Quiet", Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0) };
    private bool CodexQuotaWanted => _settings is { PrivacyMode: false } s &&
        (s.HeaderLeftMode == "Codex" || s.HeaderRightMode is "Week" or "Reset" ||
            s.ShowCodexUsage && s.AvailablePages.Any(page => s.PageHasFeature("codex", page)));
    private Control CreateCodexUsagePanel()
    {
        var stack = new StackPanel { Spacing = 12 };
        var heading = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        heading.Children.Add(new TextBlock { Text = "Cuota de Codex", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#E4F1ED"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_codexRefresh, 1); heading.Children.Add(_codexRefresh);
        ToolTip.SetTip(_codexRefresh, "Actualizar cuota"); _codexRefresh.Click += async (_, _) => await RefreshCodexQuota(true);
        stack.Children.Add(heading);
        stack.Children.Add(QuotaRow(_codexPrimaryTitle, _codexPrimaryValue, _codexPrimaryReset, _codexPrimaryLevel));
        stack.Children.Add(QuotaRow(_codexWeeklyTitle, _codexWeeklyValue, _codexWeeklyReset, _codexWeeklyLevel));
        stack.Children.Add(_codexStatus); RenderCodexQuota(); return stack;
    }
    private static Control QuotaRow(TextBlock title, TextBlock value, TextBlock reset, ProgressBar level)
    {
        title.FontSize = value.FontSize = 12; title.Foreground = Brush.Parse("#C3D7DF"); value.Foreground = Brush.Parse("#D5F0E6"); value.FontWeight = FontWeight.SemiBold;
        reset.FontSize = 11; reset.TextWrapping = TextWrapping.Wrap; reset.Foreground = Brush.Parse("#A3BBC7");
        level.Minimum = 0; level.Maximum = 100; level.Height = 3; level.Foreground = Brush.Parse("#A2DDCB"); level.Background = Brush.Parse("#2D424E");
        var row = new StackPanel { Spacing = 7 }; var line = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        line.Children.Add(title); Grid.SetColumn(value, 1); line.Children.Add(value);
        row.Children.Add(line); row.Children.Add(level); row.Children.Add(reset); return row;
    }
    private async Task RefreshCodexQuota(bool force = false)
    {
        if (!_islandReady || !CodexQuotaWanted)
        {
            if (!CodexQuotaWanted) { _codexRequest?.Cancel(); _quotaUsable = false; UpdateContextHeader(); }
            return;
        }
        if (_codexRequest is not null) return;
        var now = DateTimeOffset.UtcNow;
        bool resetPassed = new[] { _codexQuota?.Primary?.ResetsAt, _codexQuota?.Weekly?.ResetsAt }.Any(date => date > _codexAttempt && date <= now);
        if (now - _codexAttempt < TimeSpan.FromSeconds(force ? 10 : resetPassed ? 10 : 300)) return;
        _codexAttempt = now;
        var request = new CancellationTokenSource(); _codexRequest = request; var token = request.Token;
        _codexRefresh.IsEnabled = false; _codexStatus.Text = "Consultando tu sesión local de Codex…";
        try
        {
            _codexQuotaReader ??= new CodexQuotaService();
            var result = await _codexQuotaReader.ReadAsync(token);
            if (token.IsCancellationRequested || !_islandReady) return;
            _codexQuota = result; _quotaUsable = result.Available;
            _codexStatus.Text = result.Available ? $"Actualizado {result.UpdatedAt.ToLocalTime():HH:mm} · cuenta de Codex" : "La sesión no entregó datos de cuota. Comprueba tu cuenta en Codex.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException) { _quotaUsable = false; _codexStatus.Text = "Codex tardó demasiado. Puedes volver a actualizar."; }
        catch (Exception ex)
        {
            _quotaUsable = false;
            _codexStatus.Text = ex is InvalidOperationException ? ex.Message : "No se pudo leer la cuota. Abre Codex y comprueba tu sesión.";
        }
        finally
        {
            if (ReferenceEquals(_codexRequest, request)) _codexRequest = null;
            request.Dispose(); _codexRefresh.IsEnabled = true;
            if (_islandReady) { RenderCodexQuota(); UpdateContextHeader(); ArrangeContentSections(); if (_expanded) AnimateTo(ExpandedWidth, ExpandedHeightForContent); }
        }
    }
    private void RenderCodexQuota()
    {
        void Render(CodexQuotaWindow? window, string fallback, TextBlock title, TextBlock value, TextBlock reset, ProgressBar bar)
        {
            title.Text = window?.WindowLabel ?? fallback;
            value.Text = _quotaUsable && window is not null ? $"{window.RemainingPercent:0.#}% disponible" : "Sin datos";
            bar.IsVisible = _quotaUsable && window is not null; bar.Value = window?.RemainingPercent ?? 0;
            reset.Text = _quotaUsable && window is not null ? window.ResetLabel(DateTimeOffset.UtcNow, TimeZoneInfo.Local) : "";
            value.Foreground = Brush.Parse(window?.Exhausted == true && _quotaUsable ? "#E9A1AA" : "#D5F0E6");
        }
        Render(_codexQuota?.Primary, "Ventana corta", _codexPrimaryTitle, _codexPrimaryValue, _codexPrimaryReset, _codexPrimaryLevel);
        Render(_codexQuota?.Weekly, "Semana", _codexWeeklyTitle, _codexWeeklyValue, _codexWeeklyReset, _codexWeeklyLevel);
    }
    private void UpdateContextHeader()
    {
        if (_settings is not { } s) return;
        bool quota = _quotaUsable && CodexQuotaWanted && !s.PrivacyMode;
        string Compact(CodexQuotaWindow? window, string label) => quota && window is not null ? $"{label} · {window.RemainingPercent:0.#}%" : $"{label} · —";
        string timer = Productivity?.Enabled == true ? Productivity.Engine.State.TimerDeadlineUtc.HasValue ? $"Quedan {Productivity.TimerText}" :
            Productivity.Engine.State.PomodoroDeadlineUtc.HasValue ? $"{(Productivity.Engine.State.PomodoroBreak ? "Pausa" : "Foco")} {Productivity.PomodoroText}" : "" : "";
        string audio = _controlVm?.IsInputMuted == true ? "Mic · silencio" : _controlVm?.IsOutputMuted == true ? "Sin sonido" : "";
        string primaryLabel = _codexQuota?.Primary?.WindowMinutes is int minutes && minutes % 60 == 0 ? $"{minutes / 60} h" : "Codex";
        string left = s.HeaderLeftMode switch
        {
            "Codex" => s.PrivacyMode ? "" : Compact(_codexQuota?.Primary, primaryLabel),
            "Timer" => timer, "Audio" => audio, "None" => "",
            _ => timer.Length > 0 ? timer : quota ? Compact(_codexQuota?.Primary, primaryLabel) : audio
        };
        string rightMode = s.HeaderRightMode == "Auto" ? quota ? "Week" : "Clock" : s.HeaderRightMode;
        string right = rightMode == "Week" && !s.PrivacyMode ? Compact(_codexQuota?.Weekly, "Sem") : "";
        if (rightMode == "Reset" && !quota && !s.PrivacyMode) right = "Codex · —";
        ToolTip.SetTip(HeaderRightText, !quota && rightMode is "Week" or "Reset" ? _codexStatus.Text : null);
        if (rightMode is "Week" or "Reset" && quota)
        {
            var windows = new[] { _codexQuota?.Primary, _codexQuota?.Weekly }.OfType<CodexQuotaWindow>().Where(w => w.ResetsAt > DateTimeOffset.UtcNow).ToArray();
            var exhausted = windows.Where(w => w.Exhausted).ToArray();
            var reset = exhausted.Length > 0 ? exhausted.MaxBy(w => w.ResetsAt) : windows.MinBy(w => w.ResetsAt);
            if (reset?.ResetsAt is { } at && (rightMode == "Reset" || exhausted.Length > 0))
                right = at.ToLocalTime().Date == DateTime.Now.Date ? $"Vuelve {at.ToLocalTime():HH:mm}" : $"{at.ToLocalTime():ddd HH:mm}";
            ToolTip.SetTip(HeaderRightText, reset?.ResetLabel(DateTimeOffset.UtcNow, TimeZoneInfo.Local));
        }
        _headerRightUsesClock = rightMode == "Clock";
        HeaderLeftText.Text = left; HeaderRightText.Text = right;
        HeaderLeftText.IsVisible = (_expanded || s.HeaderLeftMode != "Auto") && left.Length > 0;
        HeaderRightText.IsVisible = (_expanded || s.HeaderRightMode != "Auto") && right.Length > 0;
        ToolTip.SetTip(HeaderLeftText, s.HeaderLeftMode == "Codex" && !quota ? _codexStatus.Text : quota && s.HeaderLeftMode is "Auto" or "Codex" ? _codexQuota?.Primary?.ResetLabel(DateTimeOffset.UtcNow, TimeZoneInfo.Local) : left);
        ClockText.IsVisible = s.HeaderRightMode == "Clock" || s.HeaderRightMode == "Auto" &&
            (!_expanded && s.ClockInCompact || _expanded && _headerRightUsesClock && s.ClockInExpanded);
    }
    private void OnHeaderAudioChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ViewModels.MainViewModel.IsInputMuted) or nameof(ViewModels.MainViewModel.IsOutputMuted))
            Dispatcher.UIThread.Post(UpdateContextHeader);
    }
    private void DisposeCodexQuota()
    {
        _codexRequest?.Cancel(); _codexRequest?.Dispose(); _codexRequest = null; _quotaUsable = false;
    }
}
