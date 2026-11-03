using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DaxDiff.Desktop.Views;

/// <summary>
/// Adds Ctrl+click (toggle) and Shift+click (range) selection to a <see cref="TreeView"/>, which on its own selects
/// one item. A plain click or the arrow keys select a single item as usual. Selected items are marked with the
/// attached <see cref="IsMultiSelectedProperty"/>, which the item style highlights.
/// </summary>
public sealed class TreeMultiSelect
{
    public static readonly DependencyProperty IsMultiSelectedProperty =
        DependencyProperty.RegisterAttached("IsMultiSelected", typeof(bool), typeof(TreeMultiSelect), new PropertyMetadata(false));

    public static bool GetIsMultiSelected(DependencyObject item) => (bool)item.GetValue(IsMultiSelectedProperty);

    public static void SetIsMultiSelected(DependencyObject item, bool value) => item.SetValue(IsMultiSelectedProperty, value);

    private readonly TreeView _tree;
    private readonly Func<object, IEnumerable?> _children;
    private readonly Func<object, bool> _isShown;
    private readonly HashSet<object> _selected = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _expanded = new(ReferenceEqualityComparer.Instance);
    private object? _anchor;
    private bool _updating;

    /// <param name="children">The child items of a data item, or null for a leaf (used for tree order).</param>
    /// <param name="isShown">False for an item the tree currently hides; it is skipped and never counts as selected.</param>
    public TreeMultiSelect(TreeView tree, Func<object, IEnumerable?> children, Func<object, bool>? isShown = null)
    {
        _tree = tree;
        _children = children;
        _isShown = isShown ?? (_ => true);
        tree.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        tree.SelectedItemChanged += OnSelectedItemChanged;
        tree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler((_, e) => Track(e, expanded: true)));
        tree.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler((_, e) => Track(e, expanded: false)));
    }

    /// <summary>Raised when the set of selected items changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// The selected items in tree order. Items no longer in the tree (after a search or refresh) or hidden by it are
    /// left out.
    /// </summary>
    public IReadOnlyList<object> SelectedItems => AllItems().Where(_selected.Contains).ToList();

    /// <summary>Marks a newly created item container; call it from the item style's Loaded handler.</summary>
    public void Refresh(TreeViewItem item) => SetIsMultiSelected(item, item.DataContext is { } data && _selected.Contains(data));

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemAt(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not { } data)
            return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Shift) && _anchor is not null)
        {
            var visible = VisibleItems();
            int from = visible.IndexOf(_anchor), to = visible.IndexOf(data);
            if (from < 0 || to < 0)
                return;
            _selected.Clear();
            foreach (var each in visible.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1))
                _selected.Add(each);
            e.Handled = true; // keep the anchor where it is
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_selected.Remove(data))
                _selected.Add(data);
            else if (item.IsSelected)
                Unselect(item);
            _anchor = data;
            e.Handled = true; // the TreeView would otherwise make this the only selected item
        }
        else
        {
            // A plain click: the TreeView selects the item; make it the only one (also when it was selected already).
            _selected.Clear();
            _selected.Add(data);
            _anchor = data;
        }

        Changed();
    }

    // Keyboard navigation and plain clicks: one selected item, as in a normal tree.
    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_updating)
            return;
        _selected.Clear();
        if (e.NewValue is { } data)
        {
            _selected.Add(data);
            _anchor = data;
        }

        Changed();
    }

    private void Unselect(TreeViewItem item)
    {
        _updating = true;
        try
        {
            item.IsSelected = false;
        }
        finally
        {
            _updating = false;
        }
    }

    private void Changed()
    {
        foreach (var item in Containers(_tree))
            Refresh(item);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Track(RoutedEventArgs e, bool expanded)
    {
        if ((e.OriginalSource as TreeViewItem)?.DataContext is not { } data)
            return;
        if (expanded)
            _expanded.Add(data);
        else
            _expanded.Remove(data);
    }

    // The item under the mouse; clicks on an expander arrow are left to the tree.
    private static TreeViewItem? ItemAt(DependencyObject? element)
    {
        for (var current = element; current is not null; current = Parent(current))
        {
            if (current is ToggleButton)
                return null;
            if (current is TreeViewItem item)
                return item;
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    private IEnumerable<object> AllItems() => Walk(_tree.ItemsSource, onlyExpanded: false);

    private List<object> VisibleItems() => Walk(_tree.ItemsSource, onlyExpanded: true).ToList();

    private IEnumerable<object> Walk(IEnumerable? items, bool onlyExpanded)
    {
        if (items is null)
            yield break;
        foreach (var item in items)
        {
            if (!_isShown(item))
                continue;
            yield return item;
            if (!onlyExpanded || _expanded.Contains(item))
            {
                foreach (var child in Walk(_children(item), onlyExpanded))
                    yield return child;
            }
        }
    }

    private static IEnumerable<TreeViewItem> Containers(ItemsControl parent)
    {
        foreach (var data in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(data) is not TreeViewItem item)
                continue;
            yield return item;
            foreach (var child in Containers(item))
                yield return child;
        }
    }
}
