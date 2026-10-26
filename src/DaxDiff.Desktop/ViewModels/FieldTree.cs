using DaxDiff.Domain;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>A searchable table → field tree for one model (FR-005).</summary>
public sealed record TableNode(string Name, bool IsHidden, IReadOnlyList<FieldNode> Fields)
{
    public string Display => IsHidden ? $"{Name} (hidden)" : Name;
}

public sealed record FieldNode(FieldRef Ref, DataKind DataKind, bool IsHidden, string? DisplayFolder)
{
    public bool IsMeasure => Ref.Kind == FieldKind.Measure;

    public string Display =>
        $"{(IsMeasure ? "Σ " : "")}{Ref.Name}  ·  {DataKind}{(IsHidden ? " · hidden" : "")}{(string.IsNullOrEmpty(DisplayFolder) ? "" : $" · {DisplayFolder}")}";

    public string AccessibleName => $"{(IsMeasure ? "Measure" : "Column")} {Ref.Name} in {Ref.Table}, {DataKind}";
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
}
