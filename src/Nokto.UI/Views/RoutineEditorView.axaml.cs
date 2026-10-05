using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Nokto.UI.Views;

public partial class RoutineEditorView : UserControl
{
    public RoutineEditorView() => InitializeComponent();
    private void OnAddActionClick(object? sender, RoutedEventArgs e)
    {
        // Button executes its command after raising Click; keep the flyout's data context until then.
        Dispatcher.UIThread.Post(() => this.FindControl<Button>("AddWorkflowActionButton")?.Flyout?.Hide());
    }
}
