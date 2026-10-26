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

/// <summary>A candidate model B field for a model A field. Ambiguous suggestions carry candidates and no pick (FR-007).</summary>
public sealed record MatchSuggestion
{
    public required FieldRef Left { get; init; }
    public FieldRef? Right { get; init; }
    public IReadOnlyList<FieldRef> Candidates { get; init; } = [];
    public MatchConfidence Confidence { get; init; }
    public bool IsAmbiguous => Right is null && Candidates.Count > 1;
    public required string Reason { get; init; }
}

/// <summary>A proposed synonym for an inserted model B field so that it pairs with a model A field.</summary>
public sealed record SynonymSuggestion(int IndexB, FieldRef FieldB, FieldRef FieldA, string Synonym);

/// <summary>Result of <see cref="AutoMatcher.SuggestSynonyms"/>: unique matches, plus B fields with several candidates.</summary>
public sealed record SynonymSuggestions(IReadOnlyList<SynonymSuggestion> Matches, IReadOnlyList<string> Ambiguous);

/// <summary>
/// Finds equivalent fields across the models by normalized name, table name, data type and an optional synonym
/// dictionary. Suggestions are only proposals: they are shown to the user and never guessed when ambiguous (FR-007).
/// </summary>
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

    /// <summary>True when two names are equal after normalization or through the synonym dictionary.</summary>
    public bool AreEquivalent(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        return na.Length > 0 && (na == nb || Canonicalize(na) == Canonicalize(nb));
    }

    /// <summary>
    /// For every inserted model B field whose synonym matches no model A synonym, proposes the synonym of the one
    /// unpaired model A field it is equivalent to (same kind of field, equivalent name or synonym). Fields with several
    /// candidates, or several B fields wanting the same A field, are reported as ambiguous instead.
    /// </summary>
    public SynonymSuggestions SuggestSynonyms(IReadOnlyList<ComparisonField> fieldsA, IReadOnlyList<ComparisonField> fieldsB)
    {
        var synonymsA = fieldsA.Select(f => ComparisonCompiler.SynonymKey(f.Synonym)).ToHashSet(StringComparer.Ordinal);
        var synonymsB = fieldsB.Select(f => ComparisonCompiler.SynonymKey(f.Synonym)).ToHashSet(StringComparer.Ordinal);
        var openA = fieldsA.Where(f => !synonymsB.Contains(ComparisonCompiler.SynonymKey(f.Synonym))).ToList();

        var proposals = new List<(int Index, ComparisonField B, ComparisonField A)>();
        var ambiguous = new List<string>();
        for (var i = 0; i < fieldsB.Count; i++)
        {
            var b = fieldsB[i];
            if (synonymsA.Contains(ComparisonCompiler.SynonymKey(b.Synonym)))
                continue;
            var candidates = openA
                .Where(a => a.Field.Kind == b.Field.Kind &&
                            (AreEquivalent(a.Field.Name, b.Field.Name) || AreEquivalent(a.Synonym, b.Field.Name) || AreEquivalent(a.Synonym, b.Synonym)))
                .ToList();
            if (candidates.Count == 1)
                proposals.Add((i, b, candidates[0]));
            else if (candidates.Count > 1)
                ambiguous.Add($"{b.Field}: {string.Join(", ", candidates.Select(c => c.Field.ToString()))}");
        }

        var matches = new List<SynonymSuggestion>();
        foreach (var group in proposals.GroupBy(p => p.A))
        {
            if (group.Count() == 1)
            {
                var (index, b, a) = group.Single();
                matches.Add(new SynonymSuggestion(index, b.Field, a.Field, a.Synonym));
            }
            else
            {
                ambiguous.Add($"{group.Key.Field}: wanted by {string.Join(", ", group.Select(g => g.B.Field.ToString()))}");
            }
        }

        return new SynonymSuggestions(matches.OrderBy(m => m.IndexB).ToList(), ambiguous);
    }

    public MatchSuggestion SuggestMeasure(FieldRef left, ModelMetadata b)
    {
        var name = Normalize(left.Name);
        var exact = b.Measures.Where(m => Normalize(m.Name) == name).Select(m => m.Ref).ToList();
        if (exact.Count > 0)
            return Pick(left, exact, MatchConfidence.FieldName, "Same normalized measure name");

        var canonical = Canonicalize(name);
        var synonyms = b.Measures.Where(m => Canonicalize(Normalize(m.Name)) == canonical).Select(m => m.Ref).ToList();
        return Pick(left, synonyms, MatchConfidence.Synonym, "Synonym dictionary match");
    }

    public MatchSuggestion SuggestColumn(FieldRef left, ModelMetadata a, ModelMetadata b)
    {
        var table = Normalize(left.Table);
        var name = Normalize(left.Name);
        var leftKind = a.KindOf(left);

        var sameName = b.AllColumns.Where(c => Normalize(c.Name) == name).ToList();
        var qualified = sameName.Where(c => Normalize(c.Table) == table).Select(c => c.Ref).ToList();
        if (qualified.Count > 0)
            return Pick(left, qualified, MatchConfidence.QualifiedName, "Same normalized table and column");

        // Name-only matches are narrowed by data type before declaring ambiguity.
        var typed = sameName.Where(c => TypesCompatible(leftKind, c.DataKind)).Select(c => c.Ref).ToList();
        if (typed.Count > 0)
            return Pick(left, typed, MatchConfidence.FieldName, "Same normalized column name and compatible type");

        var canonical = Canonicalize(name);
        var synonyms = b.AllColumns
            .Where(c => Canonicalize(Normalize(c.Name)) == canonical && TypesCompatible(leftKind, c.DataKind))
            .Select(c => c.Ref).ToList();
        return Pick(left, synonyms, MatchConfidence.Synonym, "Synonym dictionary match");
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

    private static MatchSuggestion Pick(FieldRef left, List<FieldRef> candidates, MatchConfidence confidence, string reason) =>
        candidates.Count switch
        {
            0 => new MatchSuggestion { Left = left, Reason = "No candidate found" },
            1 => new MatchSuggestion { Left = left, Right = candidates[0], Candidates = candidates, Confidence = confidence, Reason = reason },
            _ => new MatchSuggestion { Left = left, Candidates = candidates, Confidence = confidence, Reason = $"Ambiguous: {candidates.Count} candidates ({reason.ToLowerInvariant()})" },
        };
}
