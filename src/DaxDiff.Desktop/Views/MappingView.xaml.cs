using System.Windows;
using System.Windows.Controls;
using DaxDiff.Desktop.ViewModels;

namespace DaxDiff.Desktop.Views;

public partial class MappingView : UserControl
{
    public MappingView() => InitializeComponent();

    private MappingViewModel? ViewModel => DataContext as MappingViewModel;

    // TreeView.SelectedItem is read-only, so selection is pushed to the view model here.
    private void TreeA_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } vm)
            vm.SelectedA = e.NewValue as FieldNode;
    }

    private void TreeB_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } vm)
            vm.SelectedB = e.NewValue as FieldNode;
    }
}
