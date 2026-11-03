using DaxDiff.Domain;
using System.Globalization;
using DaxDiff.Comparison.Resources;

namespace DaxDiff.Comparison;

public enum IssueSeverity
{
    Warning,
    Error,
}

public sealed record ValidationIssue(IssueSeverity Severity, string Code, string Message, string? Alias = null)
{
    /// <summary>The model the issue is about when it concerns one side only, so the UI can show it next to that side's field.</summary>
    public Side? Side { get; init; }

    public override string ToString() => $"{Severity}: {Message}";
}

/// <summary>
/// Validates a compiled comparison against model metadata before any query runs: fields exist, roles and
/// aggregations fit, paired types are comparable, filters and settings are valid (FR-006, FR-008, §4.2 step 1).
/// </summary>
public static class MappingValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ComparisonConfig config, ModelMetadata? a, ModelMetadata? b)
    {
        var issues = new List<ValidationIssue>();

        if (config.FieldPairs.Count == 0)
            issues.Add(Error("MAP-EMPTY", Strings.Map_NothingToCompare));
        else if (config.Keys.Count == 0)
            issues.Add(Warning("MAP-NO-KEY", Strings.Map_NoKey));

        foreach (var pair in config.FieldPairs)
        {
            if (string.IsNullOrWhiteSpace(pair.Alias))
                issues.Add(Error("MAP-ALIAS", Format(Strings.Map_PairNeedsSynonym, pair.Left, pair.Right), pair.Alias));

            CheckRole(issues, pair, pair.Left, pair.LeftAggregation, Side.A);
            CheckRole(issues, pair, pair.Right, pair.RightAggregation, Side.B);
            CheckExists(issues, pair.Left, a, Side.A, pair.Alias);
            CheckExists(issues, pair.Right, b, Side.B, pair.Alias);
            if (a is not null && b is not null && a.Contains(pair.Left) && b.Contains(pair.Right))
            {
                CheckTypes(issues, pair,
                    AggregationRules.ResultKind(pair.LeftAggregation, a.KindOf(pair.Left)),
                    AggregationRules.ResultKind(pair.RightAggregation, b.KindOf(pair.Right)));
            }
        }

        CheckDuplicates(issues, config.FieldPairs, p => p.Alias.Trim().ToUpperInvariant(), p => p.Alias, "MAP-DUP-ALIAS", Strings.Map_DuplicateLabel);
        foreach (var role in new[] { FieldRole.Key, FieldRole.Value })
        {
            var pairs = config.FieldPairs.Where(p => p.Role == role).ToList();
            CheckDuplicates(issues, pairs, p => Key(p.Left), p => p.Left.ToString(), "MAP-DUP-LEFT", Strings.Map_DuplicateLeft);
            CheckDuplicates(issues, pairs, p => Key(p.Right), p => p.Right.ToString(), "MAP-DUP-RIGHT", Strings.Map_DuplicateRight);
        }

        for (var i = 0; i < config.Options.Filters.Count; i++)
            ValidateFilter(issues, config.Options.Filters[i], i + 1, a, b);

        if (config.Options.Tolerance.Absolute < 0 || config.Options.Tolerance.Relative < 0)
            issues.Add(Error("OPT-TOL", Strings.Opt_NegativeTolerance));
        if (config.Options.Limits.MaxRows <= 0)
            issues.Add(Error("OPT-ROWS", Strings.Opt_MaxRows));
        if (config.Options.Limits.TimeoutSeconds <= 0)
            issues.Add(Error("OPT-TIMEOUT", Strings.Opt_Timeout));
        if (config.Options.Results.MaxRows is <= 0)
            issues.Add(Error("OPT-RESULT-ROWS", Strings.Opt_ResultRows));

        foreach (var (meta, side) in new[] { (a, Side.A), (b, Side.B) })
        {
            if (meta is null)
                continue;
            foreach (var warning in meta.Warnings)
                issues.Add(Warning("MODEL-NOTE", Format(Strings.Map_ModelNote, side, warning)) with { Side = side });
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues) => issues.Any(i => i.Severity == IssueSeverity.Error);

    private static string Key(FieldRef f) => $"{f.Kind}|{f.Table.ToUpperInvariant()}|{f.Name.ToUpperInvariant()}";

    /// <summary>Keys are unaggregated columns; values are measures, or columns with an aggregation.</summary>
    private static void CheckRole(List<ValidationIssue> issues, FieldPair pair, FieldRef field, Aggregation aggregation, Side side)
    {
        if (pair.Role == FieldRole.Key)
        {
            if (field.Kind != FieldKind.Column)
                issues.Add(Error("MAP-ROLE", Format(Strings.Map_KeyMustBeColumn, pair.Alias, side, field), pair.Alias) with { Side = side });
            else if (aggregation != Aggregation.None)
                issues.Add(Error("MAP-AGG", Format(Strings.Map_KeyAggregated, pair.Alias, side, field), pair.Alias) with { Side = side });
        }
        else if (field.Kind == FieldKind.Measure && aggregation != Aggregation.None)
        {
            issues.Add(Error("MAP-AGG", Format(Strings.Map_MeasureAggregated, pair.Alias, side, field), pair.Alias) with { Side = side });
        }
        else if (field.Kind == FieldKind.Column && aggregation == Aggregation.None)
        {
            issues.Add(Error("MAP-AGG", Format(Strings.Map_ColumnNeedsAggregation, pair.Alias, side, field), pair.Alias) with { Side = side });
        }
    }

    private static void CheckExists(List<ValidationIssue> issues, FieldRef field, ModelMetadata? meta, Side side, string alias)
    {
        if (meta is not null && !meta.Contains(field))
            issues.Add(Error("MAP-MISSING", Format(Strings.Map_Missing, alias, field, side), alias) with { Side = side });
    }

    private static void CheckTypes(List<ValidationIssue> issues, FieldPair pair, DataKind left, DataKind right)
    {
        if (AutoMatcher.TypesCompatible(left, right))
            return;

        if (pair.Role == FieldRole.Key)
            issues.Add(Error("MAP-TYPE", Format(Strings.Map_KeyTypesDiffer, pair.Alias, left, right), pair.Alias));
        else
            issues.Add(Warning("MAP-TYPE", Format(Strings.Map_ValueTypesDiffer, pair.Alias, left, right), pair.Alias));
    }

    private static void CheckDuplicates<T>(
        List<ValidationIssue> issues, IEnumerable<T> items, Func<T, string> key, Func<T, string> display, string code, string format)
    {
        foreach (var group in items.GroupBy(key).Where(g => g.Count() > 1))
            issues.Add(Error(code, Format(format, display(group.First()))));
    }

    /// <summary>Checks one filter: columns exist on both sides and every value or condition is valid for the column type (FR-020).</summary>
    public static IReadOnlyList<ValidationIssue> ValidateFilter(FilterPair filter, int number, ModelMetadata? a, ModelMetadata? b)
    {
        var issues = new List<ValidationIssue>();
        ValidateFilter(issues, filter, number, a, b);
        return issues;
    }

    /// <summary>Checks the filter being edited in the filter pop-up; messages say "this filter" instead of a number.</summary>
    public static IReadOnlyList<ValidationIssue> ValidateFilter(FilterPair filter, ModelMetadata? a, ModelMetadata? b)
    {
        var issues = new List<ValidationIssue>();
        ValidateFilter(issues, filter, null, a, b);
        return issues;
    }

    private static void ValidateFilter(List<ValidationIssue> issues, FilterPair filter, int? number, ModelMetadata? a, ModelMetadata? b)
    {
        // Messages name the filter by number, except in the pop-up where only one filter is being edited.
        string Text(string numbered, string single, params object?[] args) =>
            number is { } n ? Format(numbered, [n, .. args]) : Format(single, args);

        if (filter.Kind == FilterKind.Basic && filter.Values.Count == 0 && !filter.IncludeBlank)
            issues.Add(Error("FLT-EMPTY", Text(Strings.Flt_NoValues, Strings.FltItem_NoValues)));
        if (filter.Kind == FilterKind.Advanced && filter.Conditions.Count is 0 or > 2)
            issues.Add(Error("FLT-CONDITIONS", Text(Strings.Flt_Conditions, Strings.FltItem_Conditions)));
        if (filter.Kind == FilterKind.Advanced)
        {
            foreach (var condition in filter.Conditions.Where(c => FilterPair.NeedsValue(c.Operator) && c.Value.Length == 0))
                issues.Add(Error("FLT-VALUE", Text(Strings.Flt_EnterValue, Strings.FltItem_EnterValue, FilterPair.OperatorText(condition.Operator))));
        }

        foreach (var (column, meta, side) in new[] { (filter.LeftColumn, a, Side.A), (filter.RightColumn, b, Side.B) })
        {
            if (column.Kind != FieldKind.Column)
            {
                issues.Add(Error("FLT-KIND", Text(Strings.Flt_TargetColumn, Strings.FltItem_TargetColumn, side)) with { Side = side });
                continue;
            }

            if (meta is null)
                continue;
            if (!meta.Contains(column))
            {
                issues.Add(Error("FLT-MISSING", Text(Strings.Flt_Missing, Strings.FltItem_Missing, column, side)) with { Side = side });
                continue;
            }

            var kind = meta.KindOf(column);
            var typedValues = filter.Kind == FilterKind.Basic
                ? filter.Values
                : filter.Conditions.Where(c => c.Operator is not (ConditionOperator.Contains or ConditionOperator.DoesNotContain
                        or ConditionOperator.StartsWith or ConditionOperator.DoesNotStartWith) && FilterPair.NeedsValue(c.Operator) && c.Value.Length > 0)
                    .Select(c => c.Value);
            foreach (var value in typedValues)
            {
                try
                {
                    DaxQueryBuilder.TypedLiteral(value, kind);
                }
                catch (FormatException ex)
                {
                    issues.Add(Error("FLT-VALUE", Text(Strings.Flt_Value, Strings.FltItem_Value, side, ex.Message)) with { Side = side });
                }
            }

            if (filter.Kind == FilterKind.Advanced)
            {
                var allowed = FilterPair.OperatorsFor(kind);
                foreach (var condition in filter.Conditions.Where(c => !allowed.Contains(c.Operator)))
                    issues.Add(Error("FLT-OPERATOR", Text(Strings.Flt_OperatorNotAvailable, Strings.FltItem_OperatorNotAvailable, side, FilterPair.OperatorText(condition.Operator), kind)) with { Side = side });
            }
        }
    }

    private static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static ValidationIssue Error(string code, string message, string? alias = null) => new(IssueSeverity.Error, code, message, alias);

    private static ValidationIssue Warning(string code, string message, string? alias = null) => new(IssueSeverity.Warning, code, message, alias);
}
