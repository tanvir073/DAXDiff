using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using DaxDiff.Desktop.Resources;
using DaxDiff.Desktop.ViewModels;
using DaxDiff.Domain;

namespace DaxDiff.App.Views;

/// <summary>The filter pop-up (FR-020). Closes with true when the user applied valid filters (<see cref="Result"/>).</summary>
public partial class FilterEditorWindow : Window
{
    private FilterEditorViewModel? _viewModel;

    public FilterEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as FilterEditorViewModel);
    }

    /// <summary>The filters to apply when the dialog closes with Apply.</summary>
    public IReadOnlyList<FilterPair> Result { get; private set; } = [];

    private void Attach(FilterEditorViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = viewModel;
        if (viewModel is not null)
            viewModel.PropertyChanged += OnViewModelChanged;
        ShowSelected();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterEditorViewModel.Selected))
            ShowSelected();
    }

    // A new editor per filter instead of one re-bound editor: when the data context of a radio group or a column list
    // changes, Avalonia can push the old selection into the newly selected filter (or the other way round).
    private void ShowSelected()
    {
        if (_viewModel?.Selected is not { } selected || Resources["FilterDetail"] is not IDataTemplate template)
        {
            Detail.Content = null;
            return;
        }

        var editor = template.Build(selected);
        if (editor is not null)
            editor.DataContext = selected;
        Detail.Content = editor;
    }

    // Invalid filters keep the dialog open, so nothing half-defined reaches the comparison.
    private async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } vm)
            return;
        var (filters, problems) = vm.Collect();
        if (problems.Length > 0)
        {
            var message = new MessageWindow(Strings.Filter_FixFirst + Environment.NewLine + Environment.NewLine + problems,
                Strings.Filter_FixFirstTitle, MessageButtons.Ok, MessageTone.Warning);
            await message.ShowDialog<bool?>(this);
            return;
        }

        Result = filters;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
