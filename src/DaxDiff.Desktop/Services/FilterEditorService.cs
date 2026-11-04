using DaxDiff.Desktop.ViewModels;
using DaxDiff.Desktop.Views;

namespace DaxDiff.Desktop.Services;

public sealed class FilterEditorService : IFilterEditorService
{
    public bool Edit(ComparisonEditorViewModel comparison)
    {
        var viewModel = new FilterEditorViewModel(comparison.Name, comparison.FilterContext(), comparison.Options.Filters);
        var window = new FilterEditorWindow { DataContext = viewModel, Owner = System.Windows.Application.Current?.MainWindow };
        if (window.ShowDialog() != true)
            return false;
        comparison.Options.SetFilters(window.Result);
        return true;
    }
}
