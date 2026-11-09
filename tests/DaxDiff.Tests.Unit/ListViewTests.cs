using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DaxDiff.Presentation.Services;

namespace DaxDiff.Tests.Unit;

/// <summary>The toolkit-neutral list view the shared view models filter and sort through.</summary>
public class ListViewTests
{
    [Fact]
    public void Filter_ShowsMatchingItems_AndCountsThem()
    {
        var view = new FilteredListView(new List<string> { "Revenue", "Cost", "Revenue LY" });

        view.Filter = o => ((string)o).StartsWith("Revenue", StringComparison.Ordinal);

        Assert.Equal(["Revenue", "Revenue LY"], view.Cast<string>());
        Assert.Equal(2, view.Count);
    }

    [Fact]
    public void Sort_OrdersShownItems_WithoutChangingTheSource()
    {
        var source = new List<int> { 3, 1, 2 };
        var view = new FilteredListView(source) { Sort = Comparer<object>.Create((x, y) => ((int)x).CompareTo((int)y)) };

        Assert.Equal([1, 2, 3], view.Cast<int>());
        Assert.Equal([3, 1, 2], source);
    }

    [Fact]
    public void SourceChanges_AreShown_AndRaiseAReset()
    {
        var source = new ObservableCollection<string> { "A" };
        var view = new FilteredListView(source);
        var resets = 0;
        view.CollectionChanged += (_, e) => resets += e.Action == NotifyCollectionChangedAction.Reset ? 1 : 0;

        source.Add("B");
        source.Move(1, 0);

        Assert.Equal(["B", "A"], view.Cast<string>());
        Assert.Equal(2, resets);
    }

    [Fact]
    public void Refresh_ReappliesAFilterThatDependsOnOtherState()
    {
        var search = "";
        var view = new FilteredListView(new List<string> { "Sales", "Stock" }) { Filter = o => ((string)o).Contains(search, StringComparison.Ordinal) };

        search = "Sa";
        Assert.Equal(2, view.Count);
        view.Refresh();

        Assert.Equal(["Sales"], view.Cast<string>());
    }
}
