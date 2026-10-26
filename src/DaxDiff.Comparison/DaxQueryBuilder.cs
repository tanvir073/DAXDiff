using System.Globalization;
using System.Text;
using DaxDiff.Domain;

namespace DaxDiff.Comparison;

/// <summary>
/// Generates model-specific DAX from a validated configuration (FR-009, §7.2).
/// All identifiers are escaped and filter values are emitted as typed literals; no user text is spliced in as DAX.
/// </summary>
public static class DaxQueryBuilder
{
    /// <summary>Result column name used for the compared value at <paramref name="index"/>.</summary>
    public static string ValueColumnAlias(int index) => $"V{index}";

    /// <summary>
    /// Builds the comparison query for one side. Result columns are ordinal: all key columns in order,
    /// then one column per value pair in order. A comparison with keys only returns the key combinations.
    /// </summary>
    public static string BuildComparisonQuery(ComparisonConfig config, Side side, ModelMetadata? metadata = null)
    {
        var keys = config.Keys.Select(p => Pick(p, side)).ToList();
        if (config.FieldPairs.Count == 0)
            throw new ArgumentException("At least one field pair is required.", nameof(config));

        var sb = new StringBuilder();
        var filterVars = AppendDefine(sb, config, side, metadata);

        sb.AppendLine("EVALUATE");
        sb.AppendLine("SUMMARIZECOLUMNS(");
        var args = SummarizeArguments(config, side, filterVars);
        sb.AppendLine(string.Join("," + Environment.NewLine, args.Select(a => "    " + a)));
        sb.AppendLine(")");

        if (keys.Count > 0)
            sb.AppendLine("ORDER BY " + string.Join(", ", keys.Select(ColumnRef)));

        return sb.ToString();
    }

    /// <summary>Builds a one-row query returning the number of key tuples the comparison query would return.</summary>
    public static string BuildRowEstimateQuery(ComparisonConfig config, Side side, ModelMetadata? metadata = null)
    {
        var sb = new StringBuilder();
        var filterVars = AppendDefine(sb, config, side, metadata);
        var args = SummarizeArguments(config, side, filterVars);

        sb.AppendLine("EVALUATE");
        sb.AppendLine($"ROW(\"Rows\", COUNTROWS(SUMMARIZECOLUMNS({string.Join(", ", args)})))");
        return sb.ToString();
    }

    private static List<string> SummarizeArguments(ComparisonConfig config, Side side, List<string> filterVars)
    {
        var args = new List<string>();
        args.AddRange(config.Keys.Select(p => ColumnRef(Pick(p, side))));
        args.AddRange(filterVars);
        args.AddRange(config.Values.Select((p, i) =>
            $"{StringLiteral(ValueColumnAlias(i))}, {ValueExpression(Pick(p, side), side == Side.A ? p.LeftAggregation : p.RightAggregation)}"));
        return args;
    }

    /// <summary>
    /// The DAX expression for one compared value: a measure as defined in the model, or an aggregated column (FR-017).
    /// Concatenate joins the distinct values in ascending order so both models produce comparable text.
    /// </summary>
    public static string ValueExpression(FieldRef field, Aggregation aggregation)
    {
        if (field.Kind == FieldKind.Measure)
            return MeasureRef(field);

        var col = ColumnRef(field);
        return aggregation switch
        {
            Aggregation.Sum => $"SUM({col})",
            Aggregation.Average => $"AVERAGE({col})",
            Aggregation.Min => $"MIN({col})",
            Aggregation.Max => $"MAX({col})",
            Aggregation.Count => $"COUNTA({col})",
            Aggregation.DistinctCount => $"DISTINCTCOUNT({col})",
            Aggregation.Concatenate => $"CONCATENATEX(VALUES({col}), {col}, \", \", {col}, ASC)",
            _ => throw new ArgumentException($"Column {field} needs an aggregation to be compared as a value.", nameof(aggregation)),
        };
    }

    private static List<string> AppendDefine(StringBuilder sb, ComparisonConfig config, Side side, ModelMetadata? metadata)
    {
        var vars = new List<string>();
        var filters = config.Options.Filters;
        if (filters.Count == 0)
            return vars;

        sb.AppendLine("DEFINE");
        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            var column = side == Side.A ? filter.LeftColumn : filter.RightColumn;
            var kind = metadata?.KindOf(column) ?? DataKind.Text;
            var col = ColumnRef(column);
            var list = "{ " + string.Join(", ", filter.Values.Select(v => TypedLiteral(v, kind))) + " }";
            var name = $"__DAXDiffFilter{i}";
            var expression = filter.Operator switch
            {
                FilterOperator.In => $"TREATAS({list}, {col})",
                FilterOperator.NotIn => $"FILTER(ALL({col}), NOT ({col} IN {list}))",
                _ => throw new ArgumentOutOfRangeException(nameof(config)),
            };
            sb.AppendLine($"    VAR {name} = {expression}");
            vars.Add(name);
        }

        return vars;
    }

    private static FieldRef Pick(FieldPair pair, Side side) => side == Side.A ? pair.Left : pair.Right;

    public static string TableRef(string table) => "'" + table.Replace("'", "''") + "'";

    public static string ColumnRef(FieldRef column) =>
        TableRef(column.Table) + "[" + column.Name.Replace("]", "]]") + "]";

    public static string MeasureRef(FieldRef measure) => "[" + measure.Name.Replace("]", "]]") + "]";

    public static string StringLiteral(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>Converts user-entered text to a DAX literal of the column's type. Throws on unparsable input.</summary>
    public static string TypedLiteral(string value, DataKind kind)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (kind)
        {
            case DataKind.Integer:
                if (long.TryParse(value.Trim(), NumberStyles.Integer, inv, out var l))
                    return l.ToString(inv);
                break;
            case DataKind.Decimal:
            case DataKind.Double:
                if (decimal.TryParse(value.Trim(), NumberStyles.Float, inv, out var d))
                    return d.ToString(inv);
                break;
            case DataKind.Boolean:
                if (bool.TryParse(value.Trim(), out var b))
                    return b ? "TRUE()" : "FALSE()";
                break;
            case DataKind.DateTime:
                if (DateTime.TryParse(value.Trim(), inv, DateTimeStyles.None, out var dt))
                {
                    var date = $"DATE({dt.Year}, {dt.Month}, {dt.Day})";
                    return dt.TimeOfDay == TimeSpan.Zero
                        ? date
                        : $"{date} + TIME({dt.Hour}, {dt.Minute}, {dt.Second})";
                }

                break;
            default:
                return StringLiteral(value);
        }

        throw new FormatException($"Filter value '{value}' is not a valid {kind} literal.");
    }
}
