using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DaxDiff.App.Views;
using DaxDiff.Presentation.Services;
using DaxDiff.Presentation.ViewModels;

namespace DaxDiff.App.Services;

/// <summary>Opens the filter pop-up modally over the main window (see <see cref="Modal.Run{T}"/>).</summary>
public sealed class FilterEditorService : IFilterEditorService
{
    public bool Edit(ComparisonEditorViewModel comparison)
    {
        if ((Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return false;
        var viewModel = new FilterEditorViewModel(comparison.Name, comparison.FilterContext(), comparison.Options.Filters);
        var window = new FilterEditorWindow { DataContext = viewModel };
        if (Modal.Run(() => window.ShowDialog<bool?>(owner)) != true)
            return false;
        comparison.Options.SetFilters(window.Result);
        return true;
    }
}
