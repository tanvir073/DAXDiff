using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DaxDiff.Desktop.ViewModels;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.Views;

public partial class ComparisonsView : UserControl
{
    private readonly TreeMultiSelect _selectA;
    private readonly TreeMultiSelect _selectB;

    public ComparisonsView()
    {
        InitializeComponent();
        // Ctrl+click and Shift+click select several tables and fields; Insert adds them all.
        // Fields already in the comparison are hidden in the trees.
        _selectA = new TreeMultiSelect(TreeA, Children, FieldTree.IsOffered);
        _selectB = new TreeMultiSelect(TreeB, Children, FieldTree.IsOffered);
        _selectA.SelectionChanged += (_, _) => { if (ViewModel is { } vm) vm.SelectedNodesA = _selectA.SelectedItems; };
        _selectB.SelectionChanged += (_, _) => { if (ViewModel is { } vm) vm.SelectedNodesB = _selectB.SelectedItems; };

        // Drag rows of a field grid to change the field order.
        _ = new RowDragReorder(FieldsA, MoveFields);
        _ = new RowDragReorder(FieldsB, MoveFields);
    }

    private static System.Collections.IEnumerable? Children(object node) => (node as TableNode)?.Fields;

    private void MoveFields(IReadOnlyList<object> items, object? before) =>
        ViewModel?.MoveFields(items.OfType<ComparisonFieldViewModel>().ToList(), before as ComparisonFieldViewModel);

    // Esc clears a header search box.
    private void HeaderSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is TextBox { Text.Length: > 0 } box)
        {
            box.Clear();
            e.Handled = true;
        }
    }

    private ComparisonsViewModel? ViewModel => DataContext as ComparisonsViewModel;

    // TreeView.SelectedItem is read-only, so selection is pushed to the view model here.
    private void TreeA_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } vm)
            vm.SelectedNodeA = e.NewValue;
    }

    private void TreeB_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } vm)
            vm.SelectedNodeB = e.NewValue;
    }

    // Item containers are created as the tree scrolls and expands; mark the ones that are selected.
    private void TreeItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item)
            return;
        DependencyObject? parent = item;
        while (parent is not null and not TreeView)
            parent = VisualTreeHelper.GetParent(parent);
        (ReferenceEquals(parent, TreeB) ? _selectB : _selectA).Refresh(item);
    }

    // Double-click a field to insert it. Double-clicking a table only expands it, so whole-table insert stays explicit.
    private void TreeA_MouseDoubleClick(object sender, MouseButtonEventArgs e) => InsertField(Side.A, TreeA.SelectedItem, e);

    private void TreeB_MouseDoubleClick(object sender, MouseButtonEventArgs e) => InsertField(Side.B, TreeB.SelectedItem, e);

    private void InsertField(Side side, object? node, RoutedEventArgs e)
    {
        if (node is FieldNode && ViewModel is { } vm)
        {
            vm.Insert(side, node);
            e.Handled = true;
        }
    }

    // Enter inserts the selected fields and tables, keeping insertion keyboard accessible.
    private void TreeA_KeyDown(object sender, KeyEventArgs e) => InsertOnEnter(Side.A, _selectA.SelectedItems, e);

    private void TreeB_KeyDown(object sender, KeyEventArgs e) => InsertOnEnter(Side.B, _selectB.SelectedItems, e);

    private void InsertOnEnter(Side side, IReadOnlyList<object> nodes, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && nodes.Count > 0 && ViewModel is { } vm)
        {
            vm.Insert(side, nodes);
            e.Handled = true;
        }
    }
}
