using DaxDiff.Domain;

namespace DaxDiff.Comparison;

public enum IssueSeverity
{
    Warning,
    Error,
}

public sealed record ValidationIssue(IssueSeverity Severity, string Code, string Message, string? Alias = null)
{
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
            issues.Add(Error("MAP-EMPTY", "Nothing to compare: give a model A field and a model B field the same synonym."));
        else if (config.Keys.Count == 0)
            issues.Add(Warning("MAP-NO-KEY", "No key fields: values are compared as grand totals only."));

        foreach (var pair in config.FieldPairs)
        {
            if (string.IsNullOrWhiteSpace(pair.Alias))
                issues.Add(Error("MAP-ALIAS", $"Pair {pair.Left} → {pair.Right} needs a synonym.", pair.Alias));

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

        CheckDuplicates(issues, config.FieldPairs, p => p.Alias.Trim().ToUpperInvariant(), p => p.Alias, "MAP-DUP-ALIAS", "Label '{0}' is used more than once.");
        foreach (var role in new[] { FieldRole.Key, FieldRole.Value })
        {
            var pairs = config.FieldPairs.Where(p => p.Role == role).ToList();
            CheckDuplicates(issues, pairs, p => Key(p.Left), p => p.Left.ToString(), "MAP-DUP-LEFT", "Model A field {0} is mapped more than once.");
            CheckDuplicates(issues, pairs, p => Key(p.Right), p => p.Right.ToString(), "MAP-DUP-RIGHT", "Model B field {0} is mapped more than once.");
        }

        for (var i = 0; i < config.Options.Filters.Count; i++)
            ValidateFilter(issues, config.Options.Filters[i], i + 1, a, b);

        if (config.Options.Tolerance.Absolute < 0 || config.Options.Tolerance.Relative < 0)
            issues.Add(Error("OPT-TOL", "Tolerances cannot be negative."));
        if (config.Options.Limits.MaxRows <= 0)
            issues.Add(Error("OPT-ROWS", "Maximum rows must be greater than zero."));
        if (config.Options.Limits.TimeoutSeconds <= 0)
            issues.Add(Error("OPT-TIMEOUT", "Timeout must be greater than zero."));
        if (config.Options.Results.MaxRows is <= 0)
            issues.Add(Error("OPT-RESULT-ROWS", "The result row limit must be greater than zero."));

        foreach (var (meta, side) in new[] { (a, Side.A), (b, Side.B) })
        {
            if (meta is null)
                continue;
            foreach (var warning in meta.Warnings)
                issues.Add(Warning("MODEL-NOTE", $"Model {side}: {warning}"));
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
                issues.Add(Error("MAP-ROLE", $"'{pair.Alias}': model {side} key {field} must be a column, not a measure.", pair.Alias));
            else if (aggregation != Aggregation.None)
                issues.Add(Error("MAP-AGG", $"'{pair.Alias}': model {side} key {field} cannot be aggregated.", pair.Alias));
        }
        else if (field.Kind == FieldKind.Measure && aggregation != Aggregation.None)
        {
            issues.Add(Error("MAP-AGG", $"'{pair.Alias}': model {side} measure {field} is evaluated as defined and cannot be aggregated again.", pair.Alias));
        }
        else if (field.Kind == FieldKind.Column && aggregation == Aggregation.None)
        {
            issues.Add(Error("MAP-AGG", $"'{pair.Alias}': model {side} column {field} has no aggregation. Choose one (for example Sum or Count), or mark it as a key on both sides.", pair.Alias));
        }
    }

    private static void CheckExists(List<ValidationIssue> issues, FieldRef field, ModelMetadata? meta, Side side, string alias)
    {
        if (meta is not null && !meta.Contains(field))
            issues.Add(Error("MAP-MISSING", $"'{alias}': {field} no longer exists in model {side} (renamed or deleted?). Replace it in the comparison.", alias));
    }

    private static void CheckTypes(List<ValidationIssue> issues, FieldPair pair, DataKind left, DataKind right)
    {
        if (AutoMatcher.TypesCompatible(left, right))
            return;

        if (pair.Role == FieldRole.Key)
            issues.Add(Error("MAP-TYPE", $"'{pair.Alias}': key types differ ({left} vs {right}); keys would never match. Choose compatible columns.", pair.Alias));
        else
            issues.Add(Warning("MAP-TYPE", $"'{pair.Alias}': value types differ ({left} vs {right}); values will be reported as type mismatches. Check the aggregations.", pair.Alias));
    }

    private static void CheckDuplicates<T>(
        List<ValidationIssue> issues, IEnumerable<T> items, Func<T, string> key, Func<T, string> display, string code, string format)
    {
        foreach (var group in items.GroupBy(key).Where(g => g.Count() > 1))
            issues.Add(Error(code, string.Format(format, display(group.First()))));
    }

    /// <summary>Checks one filter: columns exist on both sides and every value or condition is valid for the column type (FR-020).</summary>
    public static IReadOnlyList<ValidationIssue> ValidateFilter(FilterPair filter, int number, ModelMetadata? a, ModelMetadata? b)
    {
        var issues = new List<ValidationIssue>();
        ValidateFilter(issues, filter, number, a, b);
        return issues;
    }

    private static void ValidateFilter(List<ValidationIssue> issues, FilterPair filter, int number, ModelMetadata? a, ModelMetadata? b)
    {
        if (filter.Kind == FilterKind.Basic && filter.Values.Count == 0 && !filter.IncludeBlank)
            issues.Add(Error("FLT-EMPTY", $"Filter {number} has no values selected."));
        if (filter.Kind == FilterKind.Advanced && filter.Conditions.Count is 0 or > 2)
            issues.Add(Error("FLT-CONDITIONS", $"Filter {number} needs one or two conditions."));
        if (filter.Kind == FilterKind.Advanced)
        {
            foreach (var condition in filter.Conditions.Where(c => FilterPair.NeedsValue(c.Operator) && c.Value.Length == 0))
                issues.Add(Error("FLT-VALUE", $"Filter {number}: enter a value for \"{FilterPair.OperatorText(condition.Operator)}\"."));
        }

        foreach (var (column, meta, side) in new[] { (filter.LeftColumn, a, Side.A), (filter.RightColumn, b, Side.B) })
        {
            if (column.Kind != FieldKind.Column)
            {
                issues.Add(Error("FLT-KIND", $"Filter {number}: model {side} filter must target a column."));
                continue;
            }

            if (meta is null)
                continue;
            if (!meta.Contains(column))
            {
                issues.Add(Error("FLT-MISSING", $"Filter {number}: {column} does not exist in model {side}."));
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
                    issues.Add(Error("FLT-VALUE", $"Filter {number}, model {side}: {ex.Message}"));
                }
            }

            if (filter.Kind == FilterKind.Advanced)
            {
                var allowed = FilterPair.OperatorsFor(kind);
                foreach (var condition in filter.Conditions.Where(c => !allowed.Contains(c.Operator)))
                    issues.Add(Error("FLT-OPERATOR", $"Filter {number}, model {side}: \"{FilterPair.OperatorText(condition.Operator)}\" is not available for a {kind} column."));
            }
        }
    }

    private static ValidationIssue Error(string code, string message, string? alias = null) => new(IssueSeverity.Error, code, message, alias);

    private static ValidationIssue Warning(string code, string message, string? alias = null) => new(IssueSeverity.Warning, code, message, alias);
}
