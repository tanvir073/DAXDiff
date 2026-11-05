using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DaxDiff.App.Views;

/// <summary>
/// Lets the user reorder a <see cref="DataGrid"/>'s rows by dragging them. Dragging a selected row moves every
/// selected row. A line shows where the rows will land; the grid scrolls when the pointer nears its top or bottom.
/// Presses on the row's editors (check box, combo box, text box) are left to them. The drag stays inside the grid,
/// so it uses pointer capture rather than system drag and drop.
/// </summary>
public sealed class RowDragReorder
{
    private const double DragDistance = 4;
    private const double Edge = 24;

    private readonly DataGrid _grid;
    private readonly Action<IReadOnlyList<object>, object?> _move;
    private readonly DispatcherTimer _scrollTimer;
    private Point _start;
    private DataGridRow? _pressed;
    private object? _selectOnRelease;
    private List<object>? _dragged;
    private Point _last;
    private Border? _line;
    private DataGridRow? _lineRow;

    /// <param name="move">Moves the dragged items before the given item, or to the end when it is null.</param>
    public RowDragReorder(DataGrid grid, Action<IReadOnlyList<object>, object?> move)
    {
        _grid = grid;
        _move = move;
        // Tunnel: the press is seen before the grid selects the row under it.
        grid.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Input, (_, _) => AutoScroll());
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_grid);
        _selectOnRelease = null;
        if (!point.Properties.IsLeftButtonPressed)
        {
            _pressed = null;
            return;
        }

        var source = e.Source as Visual;
        _pressed = IsEditor(source) ? null : source?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        _start = point.Position;

        // A plain press on a row of a multi-row selection would select only that row, so the drag would lose the
        // rest. Keep the selection now and select the single row on release if no drag started.
        if (_pressed is { DataContext: { } item } && _grid.SelectedItems.Count > 1 && _grid.SelectedItems.Contains(item) &&
            e.KeyModifiers == KeyModifiers.None)
        {
            _selectOnRelease = item;
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(_grid);
        if (_dragged is not null)
        {
            _last = position;
            var (row, below) = Target(position);
            ShowLine(row, below);
            return;
        }

        if (_pressed is null || !e.GetCurrentPoint(_grid).Properties.IsLeftButtonPressed)
            return;
        var delta = position - _start;
        if (Math.Abs(delta.X) < DragDistance && Math.Abs(delta.Y) < DragDistance)
            return;

        var pressed = _pressed;
        _pressed = null;
        _selectOnRelease = null;
        // Dragging a selected row takes the whole selection; any other row goes alone.
        var items = Items();
        _dragged = pressed.DataContext is { } data && _grid.SelectedItems.Contains(data)
            ? items.Where(_grid.SelectedItems.Contains).ToList()
            : pressed.DataContext is { } single ? [single] : null;
        if (_dragged is null)
            return;
        _last = position;
        e.Pointer.Capture(_grid);
        _scrollTimer.Start();
        var (target, isBelow) = Target(position);
        ShowLine(target, isBelow);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressed = null;
        if (_selectOnRelease is { } item)
        {
            _selectOnRelease = null;
            _grid.SelectedItem = item;
        }

        if (_dragged is not { } dragged)
            return;
        var (row, below) = Target(e.GetPosition(_grid));
        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;

        object? before;
        if (row?.DataContext is not { } over)
            before = null;
        else if (!below)
            before = over;
        else
        {
            // "Below a row" is "before the next row"; the next shown row keeps a search filter in mind.
            var items = Items();
            var index = items.IndexOf(over);
            before = index >= 0 && index + 1 < items.Count ? items[index + 1] : null;
        }

        _move(dragged, before);
    }

    private void EndDrag()
    {
        _dragged = null;
        _scrollTimer.Stop();
        HideLine();
    }

    private List<object> Items() => (_grid.ItemsSource as IEnumerable)?.Cast<object>().ToList() ?? [];

    private IEnumerable<DataGridRow> Rows() =>
        _grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible && r.DataContext is not null);

    // The row under the pointer and whether the drop goes below it (lower half). Below every row: after the last.
    private (DataGridRow? Row, bool Below) Target(Point position)
    {
        DataGridRow? last = null;
        double lastTop = double.MinValue;
        foreach (var row in Rows())
        {
            if (row.TranslatePoint(default, _grid) is not { } origin)
                continue;
            if (position.Y >= origin.Y && position.Y < origin.Y + row.Bounds.Height)
                return (row, position.Y - origin.Y > row.Bounds.Height / 2);
            if (origin.Y > lastTop)
            {
                lastTop = origin.Y;
                last = row;
            }
        }

        return (last, true);
    }

    // Scrolls a row at a time while the pointer is near the top (under the column headers) or bottom edge.
    private void AutoScroll()
    {
        if (_dragged is null)
            return;
        var items = Items();
        var shown = Rows().Select(r => items.IndexOf(r.DataContext!)).Where(i => i >= 0).ToList();
        if (shown.Count == 0)
            return;
        if (_last.Y < Edge + _grid.ColumnHeaderHeight && shown.Min() > 0)
            _grid.ScrollIntoView(items[shown.Min() - 1], null);
        else if (_last.Y > _grid.Bounds.Height - Edge && shown.Max() + 1 < items.Count)
            _grid.ScrollIntoView(items[shown.Max() + 1], null);
        else
            return;
        var (row, below) = Target(_last);
        ShowLine(row, below);
    }

    private void ShowLine(DataGridRow? row, bool below)
    {
        if (row is null)
        {
            HideLine();
            return;
        }

        if (_line is null)
        {
            _line = new Border { Height = 2.5, IsHitTestVisible = false };
            _line.Background = _grid.TryFindResource("HeadingBrush", _grid.ActualThemeVariant, out var brush) && brush is IBrush b ? b : Brushes.Teal;
        }

        if (!ReferenceEquals(_lineRow, row))
        {
            HideLine();
            AdornerLayer.SetAdorner(row, _line);
            _lineRow = row;
        }

        _line.VerticalAlignment = below ? VerticalAlignment.Bottom : VerticalAlignment.Top;
    }

    private void HideLine()
    {
        if (_lineRow is not null)
            AdornerLayer.SetAdorner(_lineRow, null);
        _lineRow = null;
    }

    private static bool IsEditor(Visual? element)
    {
        for (var current = element; current is not null and not DataGridRow; current = current.GetVisualParent())
        {
            if (current is TextBox or ComboBox or ToggleButton or ScrollBar)
                return true;
        }

        return false;
    }
}
