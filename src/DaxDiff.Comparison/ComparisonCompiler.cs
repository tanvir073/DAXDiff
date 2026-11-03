using DaxDiff.Domain;
using System.Globalization;
using DaxDiff.Comparison.Resources;

namespace DaxDiff.Comparison;

/// <summary>Result of compiling a <see cref="ComparisonDefinition"/>.</summary>
public sealed record CompiledComparison(ComparisonConfig Config, IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => MappingValidator.HasErrors(Issues);
}

/// <summary>
/// Pairs the fields of a comparison across the two models by synonym (FR-018) and checks the definition:
/// keys, aggregations and synonyms (FR-006, FR-017). The compiled pairs keep model A's field order.
/// </summary>
public static class ComparisonCompiler
{
    public static string SynonymKey(string synonym) => synonym.Trim().ToUpperInvariant();

    /// <summary>
    /// Compiles the definition and validates the result against metadata and options in one list:
    /// pairing problems from the compiler, then fields, types, filters and settings from <see cref="MappingValidator"/>.
    /// </summary>
    public static CompiledComparison CompileAndValidate(ComparisonDefinition definition, ModelMetadata? a, ModelMetadata? b)
    {
        var compiled = Compile(definition, a, b);
        var validation = MappingValidator.Validate(compiled.Config, a, b)
            .Where(i => i.Code is not ("MAP-MISSING" or "MAP-EMPTY")); // already reported per field as CMP-MISSING / CMP-EMPTY
        if (compiled.Config.FieldPairs.Count == 0 && !compiled.Issues.Any(i => i.Code == "CMP-EMPTY"))
            validation = validation.Prepend(Error("MAP-EMPTY", Strings.Cmp_NothingToCompareYet));
        return compiled with { Issues = [.. compiled.Issues, .. validation] };
    }

    public static CompiledComparison Compile(ComparisonDefinition definition, ModelMetadata? a = null, ModelMetadata? b = null)
    {
        var issues = new List<ValidationIssue>();

        if (definition.FieldsA.Count == 0 && definition.FieldsB.Count == 0)
            issues.Add(Error("CMP-EMPTY", Strings.Cmp_InsertFields));

        CheckSide(issues, definition.FieldsA, a, Side.A);
        CheckSide(issues, definition.FieldsB, b, Side.B);

        // First occurrence wins when a synonym is duplicated; the duplicate is already reported as an error.
        var right = new Dictionary<string, ComparisonField>(StringComparer.Ordinal);
        foreach (var field in definition.FieldsB.Where(f => f.Synonym.Trim().Length > 0))
            right.TryAdd(SynonymKey(field.Synonym), field);

        var pairs = new List<FieldPair>();
        var paired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var left in definition.FieldsA.Where(f => f.Synonym.Trim().Length > 0))
        {
            var key = SynonymKey(left.Synonym);
            if (!paired.Add(key))
                continue;
            if (!right.TryGetValue(key, out var match))
            {
                issues.Add(left.IsKey
                    ? Error("CMP-KEY-UNPAIRED", Format(Strings.Cmp_KeyUnpairedInB, left.Synonym), left.Synonym)
                    : Warning("CMP-UNPAIRED", Format(Strings.Cmp_UnpairedA, left.Field, left.Synonym), left.Synonym) with { Side = Side.A });
                continue;
            }

            if (left.IsKey != match.IsKey)
            {
                issues.Add(Error("CMP-KEY-MISMATCH",
                    Format(Strings.Cmp_KeyOnOneSide, left.Synonym, left.IsKey ? "A" : "B"), left.Synonym));
                continue;
            }

            pairs.Add(new FieldPair
            {
                Left = left.Field,
                Right = match.Field,
                Role = left.IsKey ? FieldRole.Key : FieldRole.Value,
                Alias = left.Synonym.Trim(),
                LeftAggregation = left.Aggregation,
                RightAggregation = match.Aggregation,
            });
        }

        foreach (var field in definition.FieldsB.Where(f => f.Synonym.Trim().Length > 0 && !paired.Contains(SynonymKey(f.Synonym))))
        {
            issues.Add(field.IsKey
                ? Error("CMP-KEY-UNPAIRED", Format(Strings.Cmp_KeyUnpairedInA, field.Synonym), field.Synonym)
                : Warning("CMP-UNPAIRED", Format(Strings.Cmp_UnpairedB, field.Field, field.Synonym), field.Synonym) with { Side = Side.B });
        }

        // Keys first so result columns are ordinal: keys, then values (see DaxQueryBuilder).
        var ordered = pairs.Where(p => p.Role == FieldRole.Key).Concat(pairs.Where(p => p.Role == FieldRole.Value)).ToList();
        var config = new ComparisonConfig { FieldPairs = ordered, Options = definition.Options };

        if (ordered.Count > 0 && config.Values.Count == 0)
        {
            foreach (var (fields, side) in new[] { (definition.FieldsA, Side.A), (definition.FieldsB, Side.B) })
            {
                var tables = fields.Where(f => f.IsKey).Select(f => f.Field.Table).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (tables > 1)
                    issues.Add(Warning("CMP-KEYS-ONLY",
                        Format(Strings.Cmp_KeysOnly, side, tables)) with { Side = side });
            }
        }

        return new CompiledComparison(config, issues);
    }

    private static void CheckSide(List<ValidationIssue> issues, IReadOnlyList<ComparisonField> fields, ModelMetadata? meta, Side side)
    {
        var first = issues.Count;
        CheckFields(issues, fields, meta, side);
        // Everything found here concerns this side only.
        for (var i = first; i < issues.Count; i++)
            issues[i] = issues[i] with { Side = side };
    }

    private static void CheckFields(List<ValidationIssue> issues, IReadOnlyList<ComparisonField> fields, ModelMetadata? meta, Side side)
    {
        foreach (var group in fields.Where(f => f.Synonym.Trim().Length > 0).GroupBy(f => SynonymKey(f.Synonym)).Where(g => g.Count() > 1))
            issues.Add(Error("CMP-DUP-SYNONYM", Format(Strings.Cmp_DuplicateSynonym, side, group.First().Synonym.Trim(), group.Count()), group.First().Synonym));

        foreach (var group in fields.GroupBy(f => f.Field).Where(g => g.Count() > 1))
            issues.Add(Error("CMP-DUP-FIELD", Format(Strings.Cmp_DuplicateField, side, group.Key), group.First().Synonym));

        foreach (var field in fields)
        {
            if (field.Synonym.Trim().Length == 0)
            {
                issues.Add(Error("CMP-NO-SYNONYM", Format(Strings.Cmp_NoSynonym, side, field.Field), field.Synonym));
                continue;
            }

            if (field.IsKey && field.Field.Kind == FieldKind.Measure)
            {
                issues.Add(Error("CMP-MEASURE-KEY", Format(Strings.Cmp_MeasureKey, side, field.Field), field.Synonym));
                continue;
            }

            if (meta is not null && !meta.Contains(field.Field))
            {
                issues.Add(Error("CMP-MISSING", Format(Strings.Cmp_Missing, side, field.Field), field.Synonym));
                continue;
            }

            var dataKind = meta?.KindOf(field.Field) ?? DataKind.Unknown;

            if (!AggregationRules.IsAllowed(field.Aggregation, field.Field.Kind, dataKind, field.IsKey))
            {
                var allowed = string.Join(", ", AggregationRules.Allowed(field.Field.Kind, dataKind, field.IsKey));
                issues.Add(Error("CMP-AGGREGATION",
                    Format(Strings.Cmp_AggregationNotAllowed, side, field.Field, field.Aggregation, field.IsKey ? Strings.Cmp_KeyWord : dataKind.ToString(), allowed), field.Synonym));
            }
        }
    }

    private static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static ValidationIssue Error(string code, string message, string? alias = null) => new(IssueSeverity.Error, code, message, alias);

    private static ValidationIssue Warning(string code, string message, string? alias = null) => new(IssueSeverity.Warning, code, message, alias);
}
