using Avalonia.Controls;
using Avalonia.Interactivity;
using Nokto.Core.Models;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

/// <summary>Gestor de productividad situado junto a las rutinas, nunca en la isla.</summary>
public partial class ProductivityManagerView : UserControl
{
    public ProductivityManagerView() => InitializeComponent();

    private IslandProductivityViewModel? Productivity =>
        (DataContext as MainViewModel)?.IslandVm.Productivity;

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
        ProductivityStatusText.Text = ok ? "Alarma guardada en el gestor local." :
            "Escribe una hora HH:mm válida. Límite: 12 alarmas.";
        if (ok) AlarmTimeInput.Text = "";
    }
    private void OnRemoveAlarm(object? sender, RoutedEventArgs e)
    {
        if (AlarmsList.SelectedItem is IslandAlarmItem alarm && Productivity?.RemoveAlarm(alarm.Id) == true)
            ProductivityStatusText.Text = "Alarma eliminada.";
    }

    private void OnAddTask(object? sender, RoutedEventArgs e)
    {
        int minutes = TaskReminderChoice.SelectedIndex switch { 1 => 15, 2 => 60, _ => 0 };
        bool ok = Productivity?.AddTask(TaskInput.Text ?? "", minutes) == true;
        ProductivityStatusText.Text = ok ? "Tarea guardada en el gestor local." :
            "Escribe una tarea de hasta 120 caracteres. Límite: 30.";
        if (ok) TaskInput.Text = "";
    }
    private void OnCompleteTask(object? sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is IslandTaskItem task && Productivity?.CompleteTask(task.Id) == true)
            ProductivityStatusText.Text = "Estado de tarea actualizado.";
    }
    private void OnRemoveTask(object? sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is IslandTaskItem task && Productivity?.RemoveTask(task.Id) == true)
            ProductivityStatusText.Text = "Tarea eliminada.";
    }
}
