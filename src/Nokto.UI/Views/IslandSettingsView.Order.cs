using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nokto.UI.ViewModels;

namespace Nokto.UI.Views;

public partial class IslandSettingsView
{
    private IslandPageFeature? _draggedContent;
    private int _draggedSlot = -1;
    private Border? _dropMarker;
    private static int Slot(Control row) => int.TryParse(row.Tag?.ToString(), out int slot) ? slot : -1;

    private async void OnOrderDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: IslandPageFeature feature } handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
        int slot = Slot(handle); if (slot is < 0 or > 2) return;
        _draggedContent = feature; _draggedSlot = slot;
        var payload = new DataObject(); payload.Set("Nokto.Island.ContentOrder", feature.Id);
        try { await DragDrop.DoDragDrop(e, payload, DragDropEffects.Move); }
        finally { ClearDropMarker(); _draggedContent = null; _draggedSlot = -1; }
        e.Handled = true;
    }
    private bool AcceptOrderDrop(Border row) => _draggedContent is not null && Slot(row) == _draggedSlot &&
        row.DataContext is IslandPageFeature target && ReferenceEquals(target.Owner, _draggedContent.Owner);
    private static Border? OrderDropRow(object? sender, DragEventArgs e) => sender is Border row ? row :
        e.Source is Visual source ? source.GetVisualAncestors().Prepend(source).OfType<Border>()
            .FirstOrDefault(x => DragDrop.GetAllowDrop(x) && x.DataContext is IslandPageFeature && !x.Focusable && Slot(x) is >= 0 and <= 2) : null;
    private void OnOrderDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        if (OrderDropRow(sender, e) is not { } row || !AcceptOrderDrop(row)) return;
        ClearDropMarker(); _dropMarker = row;
        row.BorderBrush = Brush.Parse("#8ADBC8");
        row.BorderThickness = e.GetPosition(row).Y < row.Bounds.Height / 2 ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
        e.DragEffects = DragDropEffects.Move; e.Handled = true;
    }
    private void OnOrderDragLeave(object? sender, DragEventArgs e) => ClearDropMarker();
    private void OnOrderDrop(object? sender, DragEventArgs e)
    {
        if (OrderDropRow(sender, e) is not { } row || !AcceptOrderDrop(row) || row.DataContext is not IslandPageFeature target) return;
        bool after = e.GetPosition(row).Y >= row.Bounds.Height / 2;
        target.Owner.ReorderContent(_draggedContent!.Id, _draggedSlot, target.Id, after);
        ClearDropMarker(); e.Handled = true;
    }
    private void ClearDropMarker()
    {
        if (_dropMarker is not { } row) return;
        row.BorderBrush = Brushes.Transparent; row.BorderThickness = new Thickness(0, 2, 0, 0); _dropMarker = null;
    }
    private void OnOrderKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: IslandPageFeature feature } handle || !e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            e.Key is not (Key.Up or Key.Down)) return;
        feature.Owner.MoveContent(feature.Id, Slot(handle), e.Key == Key.Up ? -1 : 1); e.Handled = true;
    }
}
