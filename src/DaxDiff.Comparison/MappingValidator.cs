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

/// <summary>Validates mappings, filters and settings before any query runs (FR-006, FR-008, §4.2 step 1).</summary>
public static class MappingValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ComparisonConfig config, ModelMetadata? a, ModelMetadata? b)
    {
        var issues = new List<ValidationIssue>();

        if (config.Measures.Count == 0)
            issues.Add(Error("MAP-NO-MEASURE", "Map at least one measure pair."));
        if (config.Dimensions.Count == 0)
            issues.Add(Warning("MAP-NO-DIM", "No dimensions mapped: the comparison runs at grand-total grain only."));

        foreach (var pair in config.FieldPairs)
        {
            if (!pair.IsConfirmed)
                issues.Add(Error("MAP-UNCONFIRMED", $"Suggested pair '{pair.Alias}' has not been accepted.", pair.Alias));
            if (string.IsNullOrWhiteSpace(pair.Alias))
                issues.Add(Error("MAP-ALIAS", $"Pair {pair.Left} → {pair.Right} needs a label.", pair.Alias));

            var expected = pair.Role == FieldRole.Dimension ? FieldKind.Column : FieldKind.Measure;
            if (pair.Left.Kind != expected || pair.Right.Kind != expected)
                issues.Add(Error("MAP-ROLE", $"'{pair.Alias}': a {pair.Role.ToString().ToLowerInvariant()} pair must map {expected.ToString().ToLowerInvariant()}s on both sides.", pair.Alias));

            CheckExists(issues, pair.Left, a, Side.A, pair.Alias);
            CheckExists(issues, pair.Right, b, Side.B, pair.Alias);
            if (a is not null && b is not null && a.Contains(pair.Left) && b.Contains(pair.Right))
                CheckTypes(issues, pair, a.KindOf(pair.Left), b.KindOf(pair.Right));
        }

        CheckDuplicates(issues, config.FieldPairs, p => p.Alias.Trim().ToUpperInvariant(), p => p.Alias, "MAP-DUP-ALIAS", "Label '{0}' is used more than once.");
        foreach (var role in new[] { FieldRole.Dimension, FieldRole.Measure })
        {
            var pairs = config.FieldPairs.Where(p => p.Role == role).ToList();
            CheckDuplicates(issues, pairs, p => Key(p.Left), p => p.Left.ToString(), "MAP-DUP-LEFT", "Model A field {0} is mapped more than once.");
            CheckDuplicates(issues, pairs, p => Key(p.Right), p => p.Right.ToString(), "MAP-DUP-RIGHT", "Model B field {0} is mapped more than once.");
        }

        for (var i = 0; i < config.Filters.Count; i++)
            ValidateFilter(issues, config.Filters[i], i + 1, a, b);

        if (config.Tolerance.Absolute < 0 || config.Tolerance.Relative < 0)
            issues.Add(Error("OPT-TOL", "Tolerances cannot be negative."));
        if (config.Limits.MaxRows <= 0)
            issues.Add(Error("OPT-ROWS", "Maximum rows must be greater than zero."));
        if (config.Limits.TimeoutSeconds <= 0)
            issues.Add(Error("OPT-TIMEOUT", "Timeout must be greater than zero."));

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

    private static void CheckExists(List<ValidationIssue> issues, FieldRef field, ModelMetadata? meta, Side side, string alias)
    {
        if (meta is not null && !meta.Contains(field))
            issues.Add(Error("MAP-MISSING", $"'{alias}': {field} no longer exists in model {side} (renamed or deleted?). Remap it.", alias));
    }

    private static void CheckTypes(List<ValidationIssue> issues, FieldPair pair, DataKind left, DataKind right)
    {
        if (AutoMatcher.TypesCompatible(left, right))
            return;

        if (pair.Role == FieldRole.Dimension)
            issues.Add(Error("MAP-TYPE", $"'{pair.Alias}': key types differ ({left} vs {right}); keys would never match. Choose compatible columns.", pair.Alias));
        else
            issues.Add(Warning("MAP-TYPE", $"'{pair.Alias}': measure types differ ({left} vs {right}); values will be reported as type mismatches.", pair.Alias));
    }

    private static void CheckDuplicates<T>(
        List<ValidationIssue> issues, IEnumerable<T> items, Func<T, string> key, Func<T, string> display, string code, string format)
    {
        foreach (var group in items.GroupBy(key).Where(g => g.Count() > 1))
            issues.Add(Error(code, string.Format(format, display(group.First()))));
    }

    private static void ValidateFilter(List<ValidationIssue> issues, FilterPair filter, int number, ModelMetadata? a, ModelMetadata? b)
    {
        if (filter.Values.Count == 0)
            issues.Add(Error("FLT-EMPTY", $"Filter {number} has no values."));

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
            foreach (var value in filter.Values)
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
        }
    }

    private static ValidationIssue Error(string code, string message, string? alias = null) => new(IssueSeverity.Error, code, message, alias);

    private static ValidationIssue Warning(string code, string message, string? alias = null) => new(IssueSeverity.Warning, code, message, alias);
}
