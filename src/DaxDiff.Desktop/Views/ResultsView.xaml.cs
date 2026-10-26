using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DaxDiff.Desktop.ViewModels;

namespace DaxDiff.Desktop.Views;

/// <summary>Builds result grid columns at run time, since the grain and measures vary per comparison.</summary>
public partial class ResultsView : UserControl
{
    private ResultsViewModel? _viewModel;

    public ResultsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.ColumnsChanged -= OnColumnsChanged;
        _viewModel = DataContext as ResultsViewModel;
        if (_viewModel is not null)
            _viewModel.ColumnsChanged += OnColumnsChanged;
        BuildColumns();
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => BuildColumns();

    private void BuildColumns()
    {
        ResultsGrid.Columns.Clear();
        if (_viewModel is null)
            return;

        foreach (var column in _viewModel.Columns)
        {
            var binding = new Binding($"Cells[{column.Index}]")
            {
                Mode = BindingMode.OneWay,
                StringFormat = column.Kind switch
                {
                    ResultColumnKind.Value or ResultColumnKind.Delta => "#,##0.##########",
                    ResultColumnKind.Percent => "P2",
                    _ => null,
                },
                TargetNullValue = column.Kind is ResultColumnKind.Value or ResultColumnKind.Key ? "(Blank)" : "",
            };

            var gridColumn = new DataGridTextColumn
            {
                Header = column.Header,
                Binding = binding,
                SortMemberPath = column.Index.ToString(),
                Width = column.Kind == ResultColumnKind.Text ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                MinWidth = column.Kind == ResultColumnKind.Text ? 200 : 60,
            };

            if (column.Kind is ResultColumnKind.Value or ResultColumnKind.Delta or ResultColumnKind.Percent)
            {
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                gridColumn.ElementStyle = style;
            }
            else if (column.Kind == ResultColumnKind.Status)
            {
                gridColumn.ElementStyle = StatusStyle();
            }

            ResultsGrid.Columns.Add(gridColumn);
        }
    }

    private static Style StatusStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0)));
        foreach (var (text, color) in new[]
                 {
                     ("Match", "#E2F0D9"), ("Mismatch", "#F8CBAD"), ("Only in A", "#FFE699"), ("Only in B", "#BDD7EE"), ("Invalid key", "#D9C3E9"),
                 })
        {
            var trigger = new DataTrigger { Binding = new Binding("Cells[0]"), Value = text };
            trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty,
                new System.Windows.Media.BrushConverter().ConvertFromString(color)));
            style.Triggers.Add(trigger);
        }

        return style;
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (_viewModel is null || !int.TryParse(e.Column.SortMemberPath, out var index))
            return;

        var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in ResultsGrid.Columns)
            column.SortDirection = null;
        e.Column.SortDirection = direction;
        _viewModel.Sort(index, direction);
    }

    // With cell selection, SelectedItem is not always updated; keep the details panel in sync.
    private void Grid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (_viewModel is not null && ResultsGrid.CurrentCell.Item is ResultRowView row)
            _viewModel.SelectedRow = row;
    }
}
