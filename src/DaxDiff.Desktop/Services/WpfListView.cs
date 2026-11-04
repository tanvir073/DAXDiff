using System.Collections;
using System.Windows.Data;

namespace DaxDiff.Desktop.Services;

/// <summary>
/// <see cref="IListView"/> over a collection's WPF default view. Controls bound to the collection itself show the
/// same filter and order, which the drag-to-reorder field grids rely on.
/// </summary>
public sealed class WpfListView : IListView
{
    private readonly ListCollectionView _view;

    public WpfListView(IEnumerable source) =>
        _view = CollectionViewSource.GetDefaultView(source) as ListCollectionView ?? new ListCollectionView((IList)source);

    public Predicate<object>? Filter
    {
        get => _view.Filter;
        set => _view.Filter = value;
    }

    public IComparer? Sort
    {
        get => _view.CustomSort;
        set => _view.CustomSort = value;
    }

    public void Refresh() => _view.Refresh();

    public int Count => _view.Count;

    public object ItemsSource => _view;

    public IEnumerator GetEnumerator() => ((IEnumerable)_view).GetEnumerator();
}
