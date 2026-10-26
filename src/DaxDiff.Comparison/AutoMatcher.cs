using System.Text;
using DaxDiff.Domain;

namespace DaxDiff.Comparison;

public enum MatchConfidence
{
    /// <summary>Normalized table and field name both match.</summary>
    QualifiedName,

    /// <summary>Normalized field name matches; table differs.</summary>
    FieldName,

    /// <summary>Matched through the synonym dictionary.</summary>
    Synonym,
}

/// <summary>A candidate pairing. Always unconfirmed; ambiguous suggestions carry candidates and no pick (FR-007).</summary>
public sealed record MatchSuggestion
{
    public required FieldRef Left { get; init; }
    public required FieldRole Role { get; init; }
    public FieldRef? Right { get; init; }
    public IReadOnlyList<FieldRef> Candidates { get; init; } = [];
    public MatchConfidence Confidence { get; init; }
    public bool IsAmbiguous => Right is null && Candidates.Count > 1;
    public required string Reason { get; init; }

    public FieldPair? ToUnconfirmedPair() => Right is null
        ? null
        : new FieldPair { Left = Left, Right = Right, Role = Role, Alias = Left.Name, IsConfirmed = false };
}

/// <summary>Suggests A→B pairs by normalized name, table name, data type and an optional synonym dictionary.</summary>
public sealed class AutoMatcher
{
    private readonly Dictionary<string, string> _synonyms;

    /// <param name="synonymGroups">Groups of equivalent terms, e.g. ["Revenue", "Sales", "Turnover"].</param>
    public AutoMatcher(IEnumerable<IEnumerable<string>>? synonymGroups = null)
    {
        _synonyms = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in synonymGroups ?? [])
        {
            var terms = group.Select(Normalize).Where(t => t.Length > 0).ToList();
            if (terms.Count == 0)
                continue;
            foreach (var term in terms)
                _synonyms[term] = terms[0];
        }
    }

    /// <summary>Lower-case letters and digits only: "Total_Sales", "total sales" and "TotalSales" are equal.</summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    public IReadOnlyList<MatchSuggestion> SuggestMeasures(ModelMetadata a, ModelMetadata b) =>
        a.Measures.Where(m => !m.IsHidden).Select(m => SuggestMeasure(m.Ref, b)).ToList();

    public MatchSuggestion SuggestMeasure(FieldRef left, ModelMetadata b)
    {
        var name = Normalize(left.Name);
        var exact = b.Measures.Where(m => Normalize(m.Name) == name).Select(m => m.Ref).ToList();
        if (exact.Count > 0)
            return Pick(left, FieldRole.Measure, exact, MatchConfidence.FieldName, "Same normalized measure name");

        var canonical = Canonicalize(name);
        var synonyms = b.Measures.Where(m => Canonicalize(Normalize(m.Name)) == canonical).Select(m => m.Ref).ToList();
        return Pick(left, FieldRole.Measure, synonyms, MatchConfidence.Synonym, "Synonym dictionary match");
    }

    public MatchSuggestion SuggestColumn(FieldRef left, ModelMetadata a, ModelMetadata b)
    {
        var table = Normalize(left.Table);
        var name = Normalize(left.Name);
        var leftKind = a.KindOf(left);

        var sameName = b.AllColumns.Where(c => Normalize(c.Name) == name).ToList();
        var qualified = sameName.Where(c => Normalize(c.Table) == table).Select(c => c.Ref).ToList();
        if (qualified.Count > 0)
            return Pick(left, FieldRole.Dimension, qualified, MatchConfidence.QualifiedName, "Same normalized table and column");

        // Name-only matches are narrowed by data type before declaring ambiguity.
        var typed = sameName.Where(c => TypesCompatible(leftKind, c.DataKind)).Select(c => c.Ref).ToList();
        if (typed.Count > 0)
            return Pick(left, FieldRole.Dimension, typed, MatchConfidence.FieldName, "Same normalized column name and compatible type");

        var canonical = Canonicalize(name);
        var synonyms = b.AllColumns
            .Where(c => Canonicalize(Normalize(c.Name)) == canonical && TypesCompatible(leftKind, c.DataKind))
            .Select(c => c.Ref).ToList();
        return Pick(left, FieldRole.Dimension, synonyms, MatchConfidence.Synonym, "Synonym dictionary match");
    }

    public static bool TypesCompatible(DataKind a, DataKind b)
    {
        if (a == DataKind.Unknown || b == DataKind.Unknown)
            return true;
        return Family(a) == Family(b);
    }

    private static int Family(DataKind kind) => kind switch
    {
        DataKind.Integer or DataKind.Decimal or DataKind.Double => 1,
        DataKind.Text => 2,
        DataKind.DateTime => 3,
        DataKind.Boolean => 4,
        _ => 0,
    };

    private string Canonicalize(string normalizedName) =>
        _synonyms.TryGetValue(normalizedName, out var canonical) ? canonical : normalizedName;

    private static MatchSuggestion Pick(FieldRef left, FieldRole role, List<FieldRef> candidates, MatchConfidence confidence, string reason) =>
        candidates.Count switch
        {
            0 => new MatchSuggestion { Left = left, Role = role, Reason = "No candidate found" },
            1 => new MatchSuggestion { Left = left, Role = role, Right = candidates[0], Candidates = candidates, Confidence = confidence, Reason = reason },
            _ => new MatchSuggestion { Left = left, Role = role, Candidates = candidates, Confidence = confidence, Reason = $"Ambiguous: {candidates.Count} candidates ({reason.ToLowerInvariant()})" },
        };
}
