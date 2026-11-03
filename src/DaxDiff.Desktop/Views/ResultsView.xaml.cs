using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DaxDiff.Desktop.ViewModels;
using DaxDiff.Domain;
using DaxDiff.Export;
using DaxDiff.Desktop.Resources;

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
        {
            _viewModel.ColumnsChanged -= OnColumnsChanged;
            _viewModel.ColumnVisibilityChanged -= OnColumnVisibilityChanged;
        }

        _viewModel = DataContext as ResultsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ColumnsChanged += OnColumnsChanged;
            _viewModel.ColumnVisibilityChanged += OnColumnVisibilityChanged;
        }

        BuildColumns();
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => BuildColumns();

    private void OnColumnVisibilityChanged(object? sender, EventArgs e) => ApplyColumnVisibility();

    private void ApplyColumnVisibility()
    {
        if (_viewModel is null)
            return;
        foreach (var gridColumn in ResultsGrid.Columns)
        {
            if (gridColumn.GetValue(ResultColumnProperty) is ResultColumn column)
                gridColumn.Visibility = _viewModel.IsColumnShown(column) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static readonly DependencyProperty ResultColumnProperty =
        DependencyProperty.RegisterAttached("ResultColumn", typeof(ResultColumn), typeof(ResultsView));

    // The picker shows the current choice when it opens (a new result clears it).
    private void ColumnPicker_Opened(object? sender, EventArgs e)
    {
        if (_viewModel is null)
            return;
        _syncingPicker = true;
        try
        {
            ColumnPickerList.SelectedItems.Clear();
            foreach (var measure in _viewModel.ShownColumns)
                ColumnPickerList.SelectedItems.Add(measure);
        }
        finally
        {
            _syncingPicker = false;
        }
    }

    private bool _syncingPicker;

    private void ColumnPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPicker || _viewModel is null)
            return;
        // SelectedItems is in click order; keep the result's column order instead.
        var chosen = ColumnPickerList.SelectedItems.Cast<string>().ToHashSet();
        _viewModel.ShowOnlyColumns(_viewModel.FailedColumns.Where(chosen.Contains).ToList());
    }

    private void ColumnPicker_ShowAll(object sender, RoutedEventArgs e)
    {
        ColumnPickerList.UnselectAll();
        ColumnPickerPopup.IsOpen = false;
    }

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
                TargetNullValue = column.Kind is ResultColumnKind.Value or ResultColumnKind.Key ? Strings.Blank : "",
            };

            var gridColumn = new DataGridTextColumn
            {
                Header = column.Header,
                Binding = binding,
                SortMemberPath = column.Index.ToString(),
                Width = column.Kind == ResultColumnKind.Text ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                MinWidth = column.Kind == ResultColumnKind.Text ? 200 : 60,
                Visibility = _viewModel.IsColumnShown(column) ? Visibility.Visible : Visibility.Collapsed,
            };
            gridColumn.SetValue(ResultColumnProperty, column);

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
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 2, 8, 2)));
        // Theme tones (follow the selected theme): success, danger, tile (one side only) and warning.
        foreach (var (text, background, foreground) in new[]
                 {
                     // Cells[0] holds the localized status text, so compare with the same text.
                     (ExcelReportWriter.StatusText(RowStatus.Match), "SuccessSoftBrush", "SuccessBrush"),
                     (ExcelReportWriter.StatusText(RowStatus.Mismatch), "DangerSoftBrush", "DangerBrush"),
                     (ExcelReportWriter.StatusText(RowStatus.OnlyInA), "AccentSoftBrush", "TileTextBrush"),
                     (ExcelReportWriter.StatusText(RowStatus.OnlyInB), "AccentSoftBrush", "TileTextBrush"),
                     (ExcelReportWriter.StatusText(RowStatus.InvalidKey), "WarningSoftBrush", "WarningBrush"),
                 })
        {
            var trigger = new DataTrigger { Binding = new Binding("Cells[0]"), Value = text };
            trigger.Setters.Add(new Setter(TextBlock.BackgroundProperty, new DynamicResourceExtension(background)));
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(foreground)));
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
