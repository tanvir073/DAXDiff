using System.Globalization;
using DaxDiff.Domain;
using DaxDiff.Connectors.Core.Resources;

namespace DaxDiff.Connectors;

/// <summary>A tabular rowset addressed by column name (DMV results and INFO.VIEW DAX results).</summary>
internal sealed class Rowset
{
    private readonly Dictionary<string, int> _index;

    public Rowset(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        Rows = rows;
        _index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
            _index.TryAdd(CleanName(columns[i]), i);
    }

    public IReadOnlyList<object?[]> Rows { get; }

    /// <summary>"Table[Name]" and "[Name]" both become "Name".</summary>
    public static string CleanName(string column)
    {
        var start = column.LastIndexOf('[');
        return start >= 0 && column.EndsWith(']') ? column[(start + 1)..^1] : column;
    }

    public bool Has(string column) => _index.ContainsKey(column);

    public object? Get(object?[] row, string column) =>
        _index.TryGetValue(column, out var i) && row[i] is not DBNull ? row[i] : null;

    public string? Str(object?[] row, string column) => Get(row, column) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

    public long? Long(object?[] row, string column) =>
        Get(row, column) is { } v && long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var l) ? l : null;

    public bool Bool(object?[] row, string column) => Get(row, column) switch
    {
        bool b => b,
        null => false,
        var v => Convert.ToString(v, CultureInfo.InvariantCulture) is "1" or "true" or "True" or "TRUE",
    };
}

/// <summary>Builds <see cref="ModelMetadata"/> from TMSCHEMA DMVs (admin access: Desktop sessions, XMLA admins).</summary>
internal static class TmschemaMetadataReader
{
    public static ModelMetadata Build(
        string modelName,
        Rowset tables,
        Rowset columns,
        Rowset measures,
        Rowset? relationships,
        Rowset? calculationGroups,
        DateTimeOffset? lastRefresh)
    {
        var tableNames = new Dictionary<long, string>();
        var hiddenTables = new HashSet<long>();
        foreach (var row in tables.Rows)
        {
            if (tables.Long(row, "ID") is not { } id)
                continue;
            tableNames[id] = tables.Str(row, "Name") ?? $"Table{id}";
            if (tables.Bool(row, "IsHidden"))
                hiddenTables.Add(id);
        }

        var calcGroupTables = new HashSet<long>();
        if (calculationGroups is not null)
        {
            foreach (var row in calculationGroups.Rows)
                if (calculationGroups.Long(row, "TableID") is { } t)
                    calcGroupTables.Add(t);
        }

        var columnNames = new Dictionary<long, (long TableId, string Name)>();
        var columnsByTable = new Dictionary<long, List<ColumnInfo>>();
        foreach (var row in columns.Rows)
        {
            if (columns.Long(row, "ID") is not { } id || columns.Long(row, "TableID") is not { } tableId)
                continue;
            if (!tableNames.TryGetValue(tableId, out var tableName))
                continue;
            var name = columns.Str(row, "ExplicitName") ?? columns.Str(row, "InferredName") ?? $"Column{id}";
            columnNames[id] = (tableId, name);

            // Type 3 is the internal RowNumber column.
            if (columns.Long(row, "Type") == 3)
                continue;
            var kind = FromTmschemaType(columns.Long(row, "ExplicitDataType") ?? columns.Long(row, "InferredDataType"));
            var info = new ColumnInfo(tableName, name, kind, columns.Bool(row, "IsHidden") || hiddenTables.Contains(tableId));
            if (!columnsByTable.TryGetValue(tableId, out var list))
                columnsByTable[tableId] = list = [];
            list.Add(info);
        }

        var tableInfos = tableNames
            .OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TableInfo(t.Value, hiddenTables.Contains(t.Key), calcGroupTables.Contains(t.Key),
                columnsByTable.GetValueOrDefault(t.Key)?.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? []))
            .ToList();

        var measureInfos = measures.Rows
            .Select(row => new MeasureInfo(
                measures.Long(row, "TableID") is { } tid && tableNames.TryGetValue(tid, out var tn) ? tn : "",
                measures.Str(row, "Name") ?? "",
                FromTmschemaType(measures.Long(row, "DataType")),
                measures.Bool(row, "IsHidden"),
                measures.Str(row, "DisplayFolder")))
            .Where(m => m.Name.Length > 0)
            .OrderBy(m => m.Table, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var relationshipInfos = new List<RelationshipInfo>();
        if (relationships is not null)
        {
            foreach (var row in relationships.Rows)
            {
                var from = relationships.Long(row, "FromColumnID") is { } fc && columnNames.TryGetValue(fc, out var f) ? f : default;
                var to = relationships.Long(row, "ToColumnID") is { } tc && columnNames.TryGetValue(tc, out var t) ? t : default;
                if (from.Name is null || to.Name is null)
                    continue;
                relationshipInfos.Add(new RelationshipInfo(
                    tableNames.GetValueOrDefault(from.TableId, "?"), from.Name,
                    tableNames.GetValueOrDefault(to.TableId, "?"), to.Name,
                    relationships.Bool(row, "IsActive"),
                    relationships.Long(row, "CrossFilteringBehavior") == 2 ? "Both" : "Single",
                    $"{Cardinality(relationships.Long(row, "FromCardinality"))}:{Cardinality(relationships.Long(row, "ToCardinality"))}"));
            }
        }

        return new ModelMetadata
        {
            ModelName = modelName,
            Tables = tableInfos,
            Measures = measureInfos,
            Relationships = relationshipInfos,
            LastRefreshUtc = lastRefresh,
            Warnings = ModelWarnings.For(tableInfos, relationshipInfos),
        };
    }

    private static string Cardinality(long? value) => value == 2 ? "Many" : "One";

    /// <summary>TOM DataType enum values.</summary>
    public static DataKind FromTmschemaType(long? type) => type switch
    {
        2 => DataKind.Text,
        6 => DataKind.Integer,
        8 => DataKind.Double,
        9 => DataKind.DateTime,
        10 => DataKind.Decimal,
        11 => DataKind.Boolean,
        _ => DataKind.Unknown,
    };
}

/// <summary>Builds metadata from INFO.VIEW.* DAX functions (works without admin rights where supported, incl. REST).</summary>
internal static class InfoViewMetadataReader
{
    public const string TablesQuery = "EVALUATE INFO.VIEW.TABLES()";
    public const string ColumnsQuery = "EVALUATE INFO.VIEW.COLUMNS()";
    public const string MeasuresQuery = "EVALUATE INFO.VIEW.MEASURES()";
    public const string RelationshipsQuery = "EVALUATE INFO.VIEW.RELATIONSHIPS()";

    public static ModelMetadata Build(string modelName, Rowset tables, Rowset columns, Rowset measures, Rowset? relationships)
    {
        var hidden = tables.Rows
            .Where(r => tables.Bool(r, "IsHidden"))
            .Select(r => tables.Str(r, "Name") ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var calcGroups = tables.Has("CalculationGroupPrecedence")
            ? tables.Rows.Where(r => tables.Get(r, "CalculationGroupPrecedence") is not null).Select(r => tables.Str(r, "Name") ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var cols = columns.Rows
            .Where(r => !string.Equals(columns.Str(r, "Type"), "RowNumber", StringComparison.OrdinalIgnoreCase))
            .Select(r => new ColumnInfo(
                columns.Str(r, "Table") ?? "",
                columns.Str(r, "Name") ?? "",
                FromText(columns.Str(r, "DataType")),
                columns.Bool(r, "IsHidden") || hidden.Contains(columns.Str(r, "Table") ?? "")))
            .Where(c => c.Name.Length > 0 && !c.Name.StartsWith("RowNumber-", StringComparison.Ordinal))
            .ToList();

        var tableInfos = tables.Rows
            .Select(r => tables.Str(r, "Name") ?? "")
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new TableInfo(n, hidden.Contains(n), calcGroups.Contains(n),
                cols.Where(c => string.Equals(c.Table, n, StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        var measureInfos = measures.Rows
            .Select(r => new MeasureInfo(
                measures.Str(r, "Table") ?? "",
                measures.Str(r, "Name") ?? "",
                FromText(measures.Str(r, "DataType")),
                measures.Bool(r, "IsHidden"),
                measures.Str(r, "DisplayFolder")))
            .Where(m => m.Name.Length > 0)
            .OrderBy(m => m.Table, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var relationshipInfos = relationships?.Rows
            .Select(r => new RelationshipInfo(
                relationships.Str(r, "FromTable") ?? "?", relationships.Str(r, "FromColumn") ?? "?",
                relationships.Str(r, "ToTable") ?? "?", relationships.Str(r, "ToColumn") ?? "?",
                relationships.Bool(r, "IsActive"),
                relationships.Str(r, "CrossFilteringBehavior") ?? "",
                $"{relationships.Str(r, "FromCardinality")}:{relationships.Str(r, "ToCardinality")}"))
            .ToList() ?? [];

        return new ModelMetadata
        {
            ModelName = modelName,
            Tables = tableInfos,
            Measures = measureInfos,
            Relationships = relationshipInfos,
            Warnings = ModelWarnings.For(tableInfos, relationshipInfos),
        };
    }

    public static DataKind FromText(string? type) => type?.ToLowerInvariant() switch
    {
        null => DataKind.Unknown,
        "string" or "text" => DataKind.Text,
        "int64" or "integer" or "whole number" => DataKind.Integer,
        "double" or "decimal number" => DataKind.Double,
        "decimal" or "fixed decimal number" or "currency" => DataKind.Decimal,
        "datetime" or "date" or "date/time" => DataKind.DateTime,
        "boolean" or "true/false" => DataKind.Boolean,
        _ => DataKind.Unknown,
    };
}

/// <summary>Builds partial metadata from MDSCHEMA/DBSCHEMA rowsets: the lowest-privilege fallback.</summary>
internal static class MdschemaMetadataReader
{
    public const string MeasuresQuery = "SELECT * FROM $SYSTEM.MDSCHEMA_MEASURES";
    public const string ColumnsQuery = "SELECT * FROM $SYSTEM.DBSCHEMA_COLUMNS";

    public static ModelMetadata Build(string modelName, Rowset columns, Rowset measures)
    {
        var cols = columns.Rows
            .Where(r => (columns.Str(r, "TABLE_NAME") ?? "").StartsWith('$'))
            .Where(r => !columns.Has("COLUMN_OLAP_TYPE") || string.Equals(columns.Str(r, "COLUMN_OLAP_TYPE"), "ATTRIBUTE", StringComparison.OrdinalIgnoreCase))
            .Select(r => new ColumnInfo(
                (columns.Str(r, "TABLE_NAME") ?? "$")[1..],
                columns.Str(r, "COLUMN_NAME") ?? "",
                FromOleDbType(columns.Long(r, "DATA_TYPE")),
                false))
            .Where(c => c.Name.Length > 0 && !c.Name.StartsWith("RowNumber-", StringComparison.Ordinal))
            .ToList();

        var tables = cols.GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TableInfo(g.Key, false, false, g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        var measureInfos = measures.Rows
            .Where(r => (measures.Str(r, "MEASURE_NAME") ?? "") != "__Default measure")
            .Select(r => new MeasureInfo(
                measures.Str(r, "MEASUREGROUP_NAME") ?? "",
                measures.Str(r, "MEASURE_NAME") ?? "",
                FromOleDbType(measures.Long(r, "DATA_TYPE")),
                !measures.Bool(r, "MEASURE_IS_VISIBLE"),
                measures.Str(r, "MEASURE_DISPLAY_FOLDER")))
            .Where(m => m.Name.Length > 0)
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(m => m.Table, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModelMetadata
        {
            ModelName = modelName,
            Tables = tables,
            Measures = measureInfos,
            Warnings = [Strings.Meta_MdschemaLimited],
        };
    }

    /// <summary>OLE DB DBTYPE codes.</summary>
    public static DataKind FromOleDbType(long? type) => type switch
    {
        2 or 3 or 16 or 17 or 18 or 19 or 20 or 21 => DataKind.Integer,
        4 or 5 => DataKind.Double,
        6 or 14 or 131 => DataKind.Decimal,
        7 or 133 or 134 or 135 => DataKind.DateTime,
        11 => DataKind.Boolean,
        8 or 129 or 130 => DataKind.Text,
        _ => DataKind.Unknown,
    };
}

/// <summary>Model features that can legitimately yield differences and must be disclosed (§4.4).</summary>
internal static class ModelWarnings
{
    public static IReadOnlyList<string> For(IReadOnlyList<TableInfo> tables, IReadOnlyList<RelationshipInfo> relationships)
    {
        var warnings = new List<string>();
        var calcGroups = tables.Where(t => t.IsCalculationGroup).Select(t => t.Name).ToList();
        if (calcGroups.Count > 0)
            warnings.Add(string.Format(CultureInfo.CurrentCulture, Strings.Meta_CalculationGroups, string.Join(", ", calcGroups)));
        var inactive = relationships.Count(r => !r.IsActive);
        if (inactive > 0)
            warnings.Add(string.Format(CultureInfo.CurrentCulture, Strings.Meta_InactiveRelationships, inactive));
        var manyToMany = relationships.Count(r => r.Cardinality == "Many:Many");
        if (manyToMany > 0)
            warnings.Add(string.Format(CultureInfo.CurrentCulture, Strings.Meta_ManyToManyRelationships, manyToMany));
        var bidirectional = relationships.Count(r => r.CrossFilter == "Both");
        if (bidirectional > 0)
            warnings.Add(string.Format(CultureInfo.CurrentCulture, Strings.Meta_BidirectionalRelationships, bidirectional));
        return warnings;
    }
}
