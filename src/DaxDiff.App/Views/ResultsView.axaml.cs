using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using DaxDiff.Presentation.Resources;
using DaxDiff.Presentation.ViewModels;
using DaxDiff.Domain;

namespace DaxDiff.App.Views;

/// <summary>Builds result grid columns at run time, since the grain and measures vary per comparison.</summary>
public partial class ResultsView : UserControl
{
    // KPI tile tones: the view model labels its tiles with these texts.
    public static readonly IValueConverter KpiDanger = new FuncValueConverter<string?, bool>(key => key == Ui.Results_KpiMismatches);

    public static readonly IValueConverter KpiSuccess =
        new FuncValueConverter<string?, bool>(key => key == Ui.Results_KpiMatches || key == Ui.Results_KpiMatchRate);

    public static readonly IValueConverter KpiAccent =
        new FuncValueConverter<string?, bool>(key => key == Ui.Results_KpiOnlyInA || key == Ui.Results_KpiOnlyInB);

    private ResultsViewModel? _viewModel;
    private bool _syncingPicker;

    // The sorted column and direction, shown as an arrow in its header (the grid's own sort glyph follows only its own sorting).
    private int? _sortIndex;
    private ListSortDirection _sortDirection;

    public ResultsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
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
            if (gridColumn.Tag is ResultColumn column)
                gridColumn.IsVisible = _viewModel.IsColumnShown(column);
        }
    }

    // The picker shows the current choice when it opens (a new result clears it).
    private void ColumnPicker_Opened(object? sender, EventArgs e)
    {
        if (_viewModel is null)
            return;
        _syncingPicker = true;
        try
        {
            ColumnPickerList.SelectedItems?.Clear();
            foreach (var measure in _viewModel.ShownColumns)
                ColumnPickerList.SelectedItems?.Add(measure);
        }
        finally
        {
            _syncingPicker = false;
        }
    }

    private void ColumnPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Closing the flyout detaches the list, which drops its ItemsSource and with it the selection; that is not a choice.
        if (_syncingPicker || _viewModel is null || ColumnPickerList.ItemsSource is null)
            return;
        // SelectedItems is in click order; keep the result's column order instead.
        var chosen = (ColumnPickerList.SelectedItems?.Cast<string>() ?? []).ToHashSet();
        _viewModel.ShowOnlyColumns(_viewModel.FailedColumns.Where(chosen.Contains).ToList());
    }

    private void ColumnPicker_ShowAll(object? sender, RoutedEventArgs e)
    {
        ColumnPickerList.UnselectAll();
        ColumnPickerButton.Flyout?.Hide();
    }

    private void BuildColumns()
    {
        ResultsGrid.Columns.Clear();
        _sortIndex = null;
        if (_viewModel is null)
            return;

        foreach (var column in _viewModel.Columns)
        {
            var gridColumn = new DataGridTemplateColumn
            {
                Header = column.Header,
                Tag = column,
                CellTemplate = new FuncDataTemplate<ResultRowView>((_, _) => new ResultCell(column), supportsRecycling: true),
                // Copying cells (Ctrl+C) takes the raw values.
                ClipboardContentBinding = new ReflectionBinding($"Cells[{column.Index}]") { Mode = BindingMode.OneWay },
                SortMemberPath = column.Index.ToString(CultureInfo.InvariantCulture),
                CanUserSort = true,
                Width = column.Kind == ResultColumnKind.Text ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                MinWidth = column.Kind == ResultColumnKind.Text ? 200 : 60,
                IsVisible = _viewModel.IsColumnShown(column),
            };
            ResultsGrid.Columns.Add(gridColumn);
        }
    }

    private void Grid_Sorting(object? sender, DataGridColumnEventArgs e)
    {
        // The view model sorts its own view with a type-aware comparer; the grid must not sort a copy of its own.
        e.Handled = true;
        if (_viewModel is null || e.Column.Tag is not ResultColumn column)
            return;

        _sortDirection = _sortIndex == column.Index && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortIndex = column.Index;
        foreach (var gridColumn in ResultsGrid.Columns)
        {
            if (gridColumn.Tag is ResultColumn c)
                gridColumn.Header = c.Index == column.Index ? $"{c.Header} {(_sortDirection == ListSortDirection.Ascending ? "▲" : "▼")}" : c.Header;
        }

        _viewModel.Sort(column.Index, _sortDirection);
    }

    /// <summary>
    /// One grid cell. It formats its value itself (no binding per cell), so 100,000-row results scroll smoothly;
    /// recycled cells update when their row changes.
    /// </summary>
    private sealed class ResultCell : TextBlock
    {
        private readonly ResultColumn _column;

        public ResultCell(ResultColumn column)
        {
            _column = column;
            Classes.Add("resultCell");
            if (column.Kind is ResultColumnKind.Value or ResultColumnKind.Delta or ResultColumnKind.Percent)
                Classes.Add("number");
            else if (column.Kind == ResultColumnKind.Status)
            {
                Classes.Add("status");
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            }
        }

        protected override Type StyleKeyOverride => typeof(TextBlock);

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            var row = DataContext as ResultRowView;
            Text = row is null ? null : Format(row.Cells[_column.Index]);
            if (_column.Kind != ResultColumnKind.Status)
                return;
            var status = row?.Status;
            Classes.Set("match", status == RowStatus.Match);
            Classes.Set("mismatch", status == RowStatus.Mismatch);
            Classes.Set("oneSide", status is RowStatus.OnlyInA or RowStatus.OnlyInB);
            Classes.Set("invalidKey", status == RowStatus.InvalidKey);
        }

        // Numbers with up to ten decimals, Δ% as a percentage, BLANK for an empty key or value.
        private string Format(object? value) => value switch
        {
            null => _column.Kind is ResultColumnKind.Value or ResultColumnKind.Key ? Strings.Blank : "",
            IFormattable number when _column.Kind is ResultColumnKind.Value or ResultColumnKind.Delta && IsNumber(value) =>
                number.ToString("#,##0.##########", CultureInfo.CurrentCulture),
            IFormattable number when _column.Kind == ResultColumnKind.Percent && IsNumber(value) =>
                number.ToString("P2", CultureInfo.CurrentCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            _ => value.ToString() ?? "",
        };

        private static bool IsNumber(object value) =>
            value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }
}
