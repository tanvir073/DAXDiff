using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DaxDiff.Desktop.ViewModels;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.Views;

public partial class ComparisonsView : UserControl
{
    public ComparisonsView() => InitializeComponent();

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

    // Enter inserts the selected field or table, keeping insertion keyboard accessible.
    private void TreeA_KeyDown(object sender, KeyEventArgs e) => InsertOnEnter(Side.A, TreeA.SelectedItem, e);

    private void TreeB_KeyDown(object sender, KeyEventArgs e) => InsertOnEnter(Side.B, TreeB.SelectedItem, e);

    private void InsertOnEnter(Side side, object? node, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && node is not null && ViewModel is { } vm)
        {
            vm.Insert(side, node);
            e.Handled = true;
        }
    }
}
