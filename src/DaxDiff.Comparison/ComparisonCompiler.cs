using DaxDiff.Domain;

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
            validation = validation.Prepend(Error("MAP-EMPTY", "Nothing to compare yet: give a model A field and a model B field the same synonym."));
        return compiled with { Issues = [.. compiled.Issues, .. validation] };
    }

    public static CompiledComparison Compile(ComparisonDefinition definition, ModelMetadata? a = null, ModelMetadata? b = null)
    {
        var issues = new List<ValidationIssue>();

        if (definition.FieldsA.Count == 0 && definition.FieldsB.Count == 0)
            issues.Add(Error("CMP-EMPTY", "Insert fields from both models."));

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
                    ? Error("CMP-KEY-UNPAIRED", $"Key '{left.Synonym}' has no model B field with the same synonym.", left.Synonym)
                    : Warning("CMP-UNPAIRED", $"Model A field {left.Field} ('{left.Synonym}') has no model B counterpart and is not compared.", left.Synonym));
                continue;
            }

            if (left.IsKey != match.IsKey)
            {
                issues.Add(Error("CMP-KEY-MISMATCH",
                    $"'{left.Synonym}' is a key on model {(left.IsKey ? "A" : "B")} only. Mark it as a key on both sides or on neither.", left.Synonym));
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
                ? Error("CMP-KEY-UNPAIRED", $"Key '{field.Synonym}' has no model A field with the same synonym.", field.Synonym)
                : Warning("CMP-UNPAIRED", $"Model B field {field.Field} ('{field.Synonym}') has no model A counterpart and is not compared.", field.Synonym));
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
                        $"Model {side}: keys from {tables} tables without any compared value return every combination of their members. Add a value field to limit the rows to existing data."));
            }
        }

        return new CompiledComparison(config, issues);
    }

    private static void CheckSide(List<ValidationIssue> issues, IReadOnlyList<ComparisonField> fields, ModelMetadata? meta, Side side)
    {
        foreach (var group in fields.Where(f => f.Synonym.Trim().Length > 0).GroupBy(f => SynonymKey(f.Synonym)).Where(g => g.Count() > 1))
            issues.Add(Error("CMP-DUP-SYNONYM", $"Model {side}: synonym '{group.First().Synonym.Trim()}' is used by {group.Count()} fields. Each synonym must be unique per model.", group.First().Synonym));

        foreach (var group in fields.GroupBy(f => f.Field).Where(g => g.Count() > 1))
            issues.Add(Error("CMP-DUP-FIELD", $"Model {side}: {group.Key} is inserted more than once.", group.First().Synonym));

        foreach (var field in fields)
        {
            if (field.Synonym.Trim().Length == 0)
            {
                issues.Add(Error("CMP-NO-SYNONYM", $"Model {side}: {field.Field} needs a synonym.", field.Synonym));
                continue;
            }

            if (field.IsKey && field.Field.Kind == FieldKind.Measure)
            {
                issues.Add(Error("CMP-MEASURE-KEY", $"Model {side}: measure {field.Field} cannot be a key. Use columns as keys.", field.Synonym));
                continue;
            }

            if (meta is not null && !meta.Contains(field.Field))
            {
                issues.Add(Error("CMP-MISSING", $"Model {side}: {field.Field} no longer exists (renamed or deleted?). Replace or remove it.", field.Synonym));
                continue;
            }

            var dataKind = meta?.KindOf(field.Field) ?? DataKind.Unknown;

            if (!AggregationRules.IsAllowed(field.Aggregation, field.Field.Kind, dataKind, field.IsKey))
            {
                var allowed = string.Join(", ", AggregationRules.Allowed(field.Field.Kind, dataKind, field.IsKey));
                issues.Add(Error("CMP-AGGREGATION",
                    $"Model {side}: {field.Field} cannot use {field.Aggregation} ({(field.IsKey ? "key" : dataKind.ToString())}). Allowed: {allowed}.", field.Synonym));
            }
        }
    }

    private static ValidationIssue Error(string code, string message, string? alias = null) => new(IssueSeverity.Error, code, message, alias);

    private static ValidationIssue Warning(string code, string message, string? alias = null) => new(IssueSeverity.Warning, code, message, alias);
}
