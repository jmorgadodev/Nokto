using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nokto.Core.Services;

namespace Nokto.UI.ViewModels;

/// <summary>
/// Elemento visual interactivo para la tarjeta del Radar de Cuotas de IA en la cabina Inicio.
/// Soporta enlace de comandos fuertemente tipados en compilación (sin reflexión ni $parent).
/// </summary>
public partial class AiEnvironmentItem : ObservableObject
{
    public AiEnvironmentQuota Data { get; }
    public Action<string>? OpenRequested { get; }

    public string Id => Data.Id;
    public string Name => Data.Name;
    public string IconKey => Data.IconKey;
    public bool IsDetected => Data.IsDetected;
    public List<AiQuotaMetric> Metrics => Data.Metrics;
    public double PrimaryWindowPercent => Data.PrimaryWindowPercent;
    public string ResetTimeText => Data.ResetTimeText;
    public bool IsRecommended => Data.IsRecommended;
    public string StatusNote => Data.StatusNote;
    public bool HasExplicitQuotaMetrics => Data.HasExplicitQuotaMetrics;
    public bool CanOpen => !string.IsNullOrWhiteSpace(Data.ExecutablePath);
    public string DetectionDetailsText => CanOpen ? "Acceso local disponible" : "Datos locales detectados · ejecutable no disponible";
    public string QuotaAvailabilityText => HasExplicitQuotaMetrics ? "Cuotas disponibles en archivos locales" : "Acceso directo · sin cuota local compatible";
    private readonly Action<string, bool>? _visibilityChanged;
    private readonly Action<string, int>? _moveRequested;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(MoveUpCommand))] private bool _canMoveUp;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(MoveDownCommand))] private bool _canMoveDown;
    public void SetOrderPosition(int index, int count)
    {
        CanMoveUp = index > 0;
        CanMoveDown = index < count - 1;
    }
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _moveRequested?.Invoke(Id, -1);
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _moveRequested?.Invoke(Id, 1);
    [ObservableProperty] private bool _showInHome = true;
    partial void OnShowInHomeChanged(bool value) => _visibilityChanged?.Invoke(Id, value);

    [ObservableProperty]
    private double? _fiveHourUsagePercentage;

    [ObservableProperty]
    private double? _weeklyUsagePercentage;

    [ObservableProperty]
    private string _usageDetailsText = "";

    [ObservableProperty]
    private string _timeRemainingText = "";

    public AiEnvironmentItem(AiEnvironmentQuota data, Action<string>? openRequested = null,
        bool showInHome = true, Action<string, bool>? visibilityChanged = null, Action<string, int>? moveRequested = null)
    {
        Data = data;
        OpenRequested = openRequested;
        FiveHourUsagePercentage = data.FiveHourUsagePercentage;
        WeeklyUsagePercentage = data.WeeklyUsagePercentage;
        UsageDetailsText = data.StatusNote;
        TimeRemainingText = data.ResetTimeText;
        ShowInHome = showInHome;
        _visibilityChanged = visibilityChanged;
        _moveRequested = moveRequested;
    }

    [RelayCommand]
    public void Open()
    {
        if (!string.IsNullOrEmpty(Id))
        {
            OpenRequested?.Invoke(Id);
        }
    }
}
