using System.Windows;
using DaxDiff.Desktop.ViewModels;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.Views;

public partial class FilterEditorWindow : Window
{
    public FilterEditorWindow() => InitializeComponent();

    /// <summary>The filters to apply when the dialog closes with Apply.</summary>
    public IReadOnlyList<FilterPair> Result { get; private set; } = [];

    // Invalid filters keep the dialog open, so nothing half-defined reaches the comparison.
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEditorViewModel vm)
            return;
        var (filters, problems) = vm.Collect();
        if (problems.Length > 0)
        {
            MessageBox.Show(this, "Fix these filters first, or remove them:" + Environment.NewLine + Environment.NewLine + problems,
                "Filters", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = filters;
        DialogResult = true;
    }
}
