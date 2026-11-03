using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DaxDiff.Desktop.Views;

/// <summary>
/// Lets the user reorder a <see cref="DataGrid"/>'s rows by dragging them. Dragging a selected row moves every
/// selected row. A line shows where the rows will land; the grid scrolls when the mouse nears its top or bottom.
/// Presses on the row's editors (check box, combo box, text box) are left to them.
/// </summary>
public sealed class RowDragReorder
{
    private const string Format = "DaxDiff.Rows";

    private readonly DataGrid _grid;
    private readonly Action<IReadOnlyList<object>, object?> _move;
    private Point _start;
    private DataGridRow? _pressed;
    private InsertionAdorner? _adorner;

    /// <param name="move">Moves the dragged items before the given item, or to the end when it is null.</param>
    public RowDragReorder(DataGrid grid, Action<IReadOnlyList<object>, object?> move)
    {
        _grid = grid;
        _move = move;
        grid.AllowDrop = true;
        grid.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        grid.PreviewMouseMove += OnPreviewMouseMove;
        grid.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        grid.DragOver += OnDragOver;
        grid.DragLeave += (_, _) => HideLine();
        grid.Drop += OnDrop;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        _pressed = IsEditor(source) ? null : Find<DataGridRow>(source);
        _start = e.GetPosition(_grid);
        _selectOnRelease = null;

        // A plain press on a row of a multi-row selection would select only that row, so the drag would lose the
        // rest. Keep the selection now and select the single row on release if no drag started.
        if (_pressed is { IsSelected: true } && _grid.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _selectOnRelease = _pressed.Item;
            e.Handled = true;
        }
    }

    private object? _selectOnRelease;

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressed = null;
        if (_selectOnRelease is { } item)
        {
            _selectOnRelease = null;
            _grid.SelectedItem = item;
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var delta = e.GetPosition(_grid) - _start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var row = _pressed;
        _pressed = null;
        _selectOnRelease = null;
        // Dragging a selected row takes the whole selection; any other row goes alone.
        var items = row.IsSelected
            ? _grid.Items.Cast<object>().Where(_grid.SelectedItems.Contains).ToList()
            : [row.Item];
        try
        {
            DragDrop.DoDragDrop(_grid, new DataObject(Format, items), DragDropEffects.Move);
        }
        finally
        {
            HideLine();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(Format))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        AutoScroll(e.GetPosition(_grid).Y);
        var (row, below) = Target(e);
        ShowLine(row, below);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        HideLine();
        if (e.Data.GetData(Format) is not List<object> items)
            return;
        e.Handled = true;
        var (row, below) = Target(e);
        object? before;
        if (row is null)
            before = null;
        else if (!below)
            before = row.Item;
        else
        {
            // "Below a row" is "before the next row"; the next visible row keeps a search filter in mind.
            var index = _grid.Items.IndexOf(row.Item);
            before = index + 1 < _grid.Items.Count ? _grid.Items[index + 1] : null;
        }

        _move(items, before);
    }

    // The row under the mouse and whether the drop goes below it (lower half). No row: after the last row.
    private (DataGridRow? Row, bool Below) Target(DragEventArgs e)
    {
        var row = Find<DataGridRow>(_grid.InputHitTest(e.GetPosition(_grid)) as DependencyObject);
        if (row is null)
        {
            var last = _grid.Items.Count == 0 ? null : _grid.ItemContainerGenerator.ContainerFromIndex(_grid.Items.Count - 1) as DataGridRow;
            return (last, true);
        }

        return (row, e.GetPosition(row).Y > row.ActualHeight / 2);
    }

    private void AutoScroll(double y)
    {
        if (FindChild<ScrollViewer>(_grid) is not { } scroll)
            return;
        const double edge = 24;
        if (y < edge + 36) // the column header row sits above the rows
            scroll.LineUp();
        else if (y > _grid.ActualHeight - edge)
            scroll.LineDown();
    }

    private void ShowLine(DataGridRow? row, bool below)
    {
        if (row is null)
        {
            HideLine();
            return;
        }

        if (_adorner?.AdornedElement != row)
        {
            HideLine();
            if (AdornerLayer.GetAdornerLayer(row) is not { } layer)
                return;
            _adorner = new InsertionAdorner(row, _grid.TryFindResource("TealBrush") as Brush ?? Brushes.Teal);
            layer.Add(_adorner);
        }

        _adorner.Below = below;
    }

    private void HideLine()
    {
        if (_adorner is null)
            return;
        AdornerLayer.GetAdornerLayer(_adorner.AdornedElement)?.Remove(_adorner);
        _adorner = null;
    }

    private static bool IsEditor(DependencyObject? element)
    {
        for (var current = element; current is not null and not DataGridRow; current = Parent(current))
        {
            if (current is TextBoxBase or ComboBox or ToggleButton or ScrollBar)
                return true;
        }

        return false;
    }

    private static T? Find<T>(DependencyObject? element) where T : DependencyObject
    {
        for (var current = element; current is not null; current = Parent(current))
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if ((child as T ?? FindChild<T>(child)) is { } match)
                return match;
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    /// <summary>A line at the top or bottom edge of a row, where dragged rows will land.</summary>
    private sealed class InsertionAdorner : Adorner
    {
        private readonly Pen _pen;
        private bool _below;

        public InsertionAdorner(UIElement row, Brush brush) : base(row)
        {
            _pen = new Pen(brush, 2.5);
            IsHitTestVisible = false; // the drop target is found by hit testing under it
        }

        public bool Below
        {
            get => _below;
            set
            {
                if (_below == value)
                    return;
                _below = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var y = Below ? AdornedElement.RenderSize.Height - 1 : 1;
            drawingContext.DrawLine(_pen, new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
        }
    }
}
