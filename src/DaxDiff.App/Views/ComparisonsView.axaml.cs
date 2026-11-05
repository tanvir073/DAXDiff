using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaxDiff.Presentation.ViewModels;
using DaxDiff.Domain;

namespace DaxDiff.App.Views;

public partial class ComparisonsView : UserControl
{
    // Rows are recycled, so each row's issue highlight follows whichever field it shows at the moment.
    private readonly ConditionalWeakTable<DataGridRow, RowIssue> _rowIssues = new();

    public ComparisonsView()
    {
        InitializeComponent();

        // The trees select several tables and fields natively (SelectionMode Multiple); Insert adds them all.
        TreeA.SelectionChanged += (_, _) => PushSelection(Side.A);
        TreeB.SelectionChanged += (_, _) => PushSelection(Side.B);
        TreeA.DoubleTapped += (_, e) => InsertField(Side.A, e);
        TreeB.DoubleTapped += (_, e) => InsertField(Side.B, e);
        // Tunnel, so Enter inserts before the tree item treats the key itself.
        TreeA.AddHandler(KeyDownEvent, (_, e) => InsertOnEnter(Side.A, e), RoutingStrategies.Tunnel);
        TreeB.AddHandler(KeyDownEvent, (_, e) => InsertOnEnter(Side.B, e), RoutingStrategies.Tunnel);

        // Drag rows of a field grid to change the field order.
        _ = new RowDragReorder(FieldsA, (items, before) => MoveFields(FieldsA, items, before));
        _ = new RowDragReorder(FieldsB, (items, before) => MoveFields(FieldsB, items, before));

        FieldsA.LoadingRow += (_, e) => TrackIssue(e.Row);
        FieldsB.LoadingRow += (_, e) => TrackIssue(e.Row);
    }

    private ComparisonsViewModel? ViewModel => DataContext as ComparisonsViewModel;

    // ---- Field trees ----------------------------------------------------------------------------

    // TreeView.SelectedItems is in click order; the view model wants tree order, without nodes the tree hides.
    private void PushSelection(Side side)
    {
        if (ViewModel is not { } vm)
            return;
        var tree = side == Side.A ? TreeA : TreeB;
        var selected = new HashSet<object>(tree.SelectedItems?.Cast<object>() ?? [], ReferenceEqualityComparer.Instance);
        var nodes = (side == Side.A ? vm.TreeA : vm.TreeB)
            .SelectMany(table => new object[] { table }.Concat(table.Fields))
            .Where(node => selected.Contains(node) && FieldTree.IsOffered(node))
            .ToList();
        if (side == Side.A)
        {
            vm.SelectedNodeA = tree.SelectedItem;
            vm.SelectedNodesA = nodes;
        }
        else
        {
            vm.SelectedNodeB = tree.SelectedItem;
            vm.SelectedNodesB = nodes;
        }
    }

    // Double-click a field to insert it. Double-clicking a table only expands it, so whole-table insert stays explicit.
    private void InsertField(Side side, TappedEventArgs e)
    {
        var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (item?.DataContext is FieldNode node && ViewModel is { } vm)
        {
            vm.Insert(side, node);
            e.Handled = true;
        }
    }

    // Enter inserts the selected fields and tables, keeping insertion keyboard accessible.
    private void InsertOnEnter(Side side, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { } vm)
            return;
        var nodes = side == Side.A ? vm.SelectedNodesA : vm.SelectedNodesB;
        if (nodes.Count > 0)
        {
            vm.Insert(side, nodes);
            e.Handled = true;
        }
    }

    // ---- Field grids ----------------------------------------------------------------------------

    // Esc clears a header search box.
    private void HeaderSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is TextBox { Text.Length: > 0 } box)
        {
            box.Clear();
            e.Handled = true;
        }
    }

    // The grids show a filtered view that resets on every change, which drops the selection. Moving keeps it, so the
    // arrows can be clicked again. Click runs before the command, so the selection is read before the move.
    private void Move_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is DataGrid grid)
            KeepSelection(grid, grid.SelectedItems.Cast<object>().ToList());
    }

    private void MoveFields(DataGrid grid, IReadOnlyList<object> items, object? before)
    {
        ViewModel?.MoveFields(items.OfType<ComparisonFieldViewModel>().ToList(), before as ComparisonFieldViewModel);
        KeepSelection(grid, items);
    }

    private static void KeepSelection(DataGrid grid, IReadOnlyList<object> items) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (grid.ItemsSource is not IList shown)
                return;
            var keep = items.Where(shown.Contains).ToList();
            if (keep.Count == 0)
                return;
            grid.SelectedItems.Clear();
            foreach (var item in keep)
                grid.SelectedItems.Add(item);
            grid.ScrollIntoView(keep[0], null);
        });

    // Amber row: the field has a warning (for example not compared). Red row: an error that blocks the run.
    private void TrackIssue(DataGridRow row) => _rowIssues.GetValue(row, r => new RowIssue(r)).Attach();

    private sealed class RowIssue(DataGridRow row)
    {
        private ComparisonFieldViewModel? _field;

        public void Attach()
        {
            if (_field is not null)
                _field.PropertyChanged -= OnChanged;
            _field = row.DataContext as ComparisonFieldViewModel;
            if (_field is not null)
                _field.PropertyChanged += OnChanged;
            Update();
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ComparisonFieldViewModel.Issue) or nameof(ComparisonFieldViewModel.HasError))
                Update();
        }

        private void Update()
        {
            row.Classes.Set("issue", _field?.Issue is not null);
            row.Classes.Set("error", _field?.HasError == true);
        }
    }
}
