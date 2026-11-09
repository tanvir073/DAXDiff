using System.Collections;
using System.Collections.Specialized;

namespace DaxDiff.Presentation.Services;

/// <summary>
/// A filtered and sorted view of a collection, which a list control shows. View models set the filter and sort; the UI
/// binds to <see cref="ItemsSource"/>. The source collection itself is never changed.
/// </summary>
public interface IListView : IEnumerable
{
    Predicate<object>? Filter { get; set; }

    /// <summary>Order of the shown items; null keeps the source order.</summary>
    IComparer? Sort { get; set; }

    /// <summary>Re-applies the filter and sort, for example after the search text changed.</summary>
    void Refresh();

    /// <summary>Number of items shown.</summary>
    int Count { get; }

    /// <summary>The object a list control binds to.</summary>
    object ItemsSource { get; }
}

/// <summary>Creates list views. Each app may replace <see cref="Create"/> with its toolkit's own collection view.</summary>
public static class ListViews
{
    /// <summary>
    /// Returns the view of <paramref name="source"/>. Called once per source collection.
    /// </summary>
    public static Func<IEnumerable, IListView> Create { get; set; } = source => new FilteredListView(source);
}

/// <summary>
/// Toolkit-neutral <see cref="IListView"/>: a read-only list of the source's shown items that raises a reset whenever
/// the source changes or the view is refreshed.
/// </summary>
public sealed class FilteredListView : IListView, IList, INotifyCollectionChanged
{
    private readonly IEnumerable _source;
    private List<object> _items = [];
    private Predicate<object>? _filter;
    private IComparer? _sort;

    public FilteredListView(IEnumerable source)
    {
        _source = source;
        if (source is INotifyCollectionChanged changes)
            changes.CollectionChanged += (_, _) => Refresh();
        Rebuild();
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public Predicate<object>? Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            Refresh();
        }
    }

    public IComparer? Sort
    {
        get => _sort;
        set
        {
            _sort = value;
            Refresh();
        }
    }

    public object ItemsSource => this;

    public int Count => _items.Count;

    public void Refresh()
    {
        Rebuild();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void Rebuild()
    {
        var items = _source.Cast<object>();
        if (_filter is { } filter)
            items = items.Where(i => filter(i));
        var list = items.ToList();
        if (_sort is { } sort)
            list.Sort(sort.Compare);
        _items = list;
    }

    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    public object? this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException();
    }

    public bool IsReadOnly => true;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public bool Contains(object? value) => value is not null && _items.Contains(value);
    public int IndexOf(object? value) => value is null ? -1 : _items.IndexOf(value);
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
