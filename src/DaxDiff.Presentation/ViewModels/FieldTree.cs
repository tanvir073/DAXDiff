using CommunityToolkit.Mvvm.ComponentModel;
using DaxDiff.Domain;
using DaxDiff.Desktop.Resources;
using DaxDiff.Desktop.Services;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>A searchable table → field tree for one model (FR-005).</summary>
/// <remarks>
/// Nodes are classes, not records: they are compared by reference, so the tree keeps finding them while their
/// <see cref="FieldNode.IsInComparison"/> state changes.
/// </remarks>
public sealed partial class TableNode(string name, bool isHidden, IReadOnlyList<FieldNode> fields) : ObservableObject
{
    public string Name { get; } = name;
    public bool IsHidden { get; } = isHidden;
    public IReadOnlyList<FieldNode> Fields { get; } = fields;

    public string Display => IsHidden ? Localized.Format(Strings.Tree_Hidden, Name) : Name;

    /// <summary>Every field of the table is in the comparison, so the table is left out of the tree.</summary>
    [ObservableProperty]
    private bool _isInComparison;
}

public sealed partial class FieldNode(FieldRef fieldRef, DataKind dataKind, bool isHidden, string? displayFolder) : ObservableObject
{
    public FieldRef Ref { get; } = fieldRef;
    public DataKind DataKind { get; } = dataKind;
    public bool IsHidden { get; } = isHidden;
    public string? DisplayFolder { get; } = displayFolder;

    /// <summary>The field is already in the selected comparison, so the tree leaves it out.</summary>
    [ObservableProperty]
    private bool _isInComparison;

    public bool IsMeasure => Ref.Kind == FieldKind.Measure;

    public string Display =>
        $"{(IsMeasure ? "Σ " : "")}{Ref.Name}  ·  {EnumText.Of(DataKind)}{(IsHidden ? Strings.Tree_HiddenSuffix : "")}{(string.IsNullOrEmpty(DisplayFolder) ? "" : $" · {DisplayFolder}")}";

    public string AccessibleName =>
        Localized.Format(IsMeasure ? Strings.Tree_AccessibleMeasure : Strings.Tree_AccessibleColumn, Ref.Name, Ref.Table, EnumText.Of(DataKind));
}

public static class FieldTree
{
    public static IReadOnlyList<TableNode> Build(ModelMetadata? metadata, string? search, bool includeHidden)
    {
        if (metadata is null)
            return [];

        bool Visible(string table, string name, bool hidden) =>
            (includeHidden || !hidden) &&
            (string.IsNullOrWhiteSpace(search) ||
             name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             table.Contains(search, StringComparison.OrdinalIgnoreCase));

        var measuresByTable = metadata.Measures.ToLookup(m => m.Table, StringComparer.OrdinalIgnoreCase);
        var tableNames = metadata.Tables.Select(t => t.Name)
            .Concat(metadata.Measures.Select(m => m.Table))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        var nodes = new List<TableNode>();
        foreach (var name in tableNames)
        {
            var table = metadata.Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            var fields = measuresByTable[name]
                .Where(m => Visible(m.Table, m.Name, m.IsHidden))
                .Select(m => new FieldNode(m.Ref, m.DataKind, m.IsHidden, m.DisplayFolder))
                .Concat((table?.Columns ?? [])
                    .Where(c => Visible(c.Table, c.Name, c.IsHidden))
                    .Select(c => new FieldNode(c.Ref, c.DataKind, c.IsHidden, null)))
                .ToList();
            if (fields.Count > 0)
                nodes.Add(new TableNode(name, table?.IsHidden ?? false, fields));
        }

        return nodes;
    }

    /// <summary>Marks the fields (and whole tables) that are in the comparison, which the tree then leaves out.</summary>
    public static void MarkInComparison(IReadOnlyList<TableNode> tree, IReadOnlySet<FieldRef> inComparison)
    {
        foreach (var table in tree)
        {
            foreach (var field in table.Fields)
                field.IsInComparison = inComparison.Contains(field.Ref);
            table.IsInComparison = table.Fields.All(f => f.IsInComparison);
        }
    }

    /// <summary>False for a node the tree leaves out because it is already in the comparison.</summary>
    public static bool IsOffered(object node) => node switch
    {
        TableNode table => !table.IsInComparison,
        FieldNode field => !field.IsInComparison,
        _ => true,
    };
}
