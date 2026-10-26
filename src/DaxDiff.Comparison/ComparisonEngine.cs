using DaxDiff.Domain;

namespace DaxDiff.Comparison;

/// <summary>Result of joining and classifying two query results, before run status is decided.</summary>
public sealed record EngineOutput
{
    public required IReadOnlyList<ComparisonRow> Rows { get; init; }
    public required ComparisonSummary Summary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Duplicate normalized keys were found on either side.</summary>
    public bool HasDuplicateKeys { get; init; }
}

/// <summary>
/// Normalizes keys, full-outer-joins the two result sets, compares paired measures and computes statistics (§4.2).
/// </summary>
public static class ComparisonEngine
{
    public static EngineOutput Compare(QueryResult a, QueryResult b, ComparisonConfig config)
    {
        var dimCount = config.Dimensions.Count;
        var measurePairs = config.Measures;
        var expectedColumns = dimCount + measurePairs.Count;
        EnsureShape(a, expectedColumns, Side.A);
        EnsureShape(b, expectedColumns, Side.B);

        var warnings = new List<string>();
        var indexA = Index(a, dimCount, config.KeyNormalization);
        var indexB = Index(b, dimCount, config.KeyNormalization);

        var rows = new List<ComparisonRow>(Math.Max(a.Rows.Count, b.Rows.Count));
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        duplicateKeys.UnionWith(indexA.Duplicates.Keys);
        duplicateKeys.UnionWith(indexB.Duplicates.Keys);

        foreach (var key in duplicateKeys.Order(StringComparer.Ordinal))
        {
            indexA.Duplicates.TryGetValue(key, out var countA);
            indexB.Duplicates.TryGetValue(key, out var countB);
            var rowA = indexA.Rows.GetValueOrDefault(key);
            var rowB = indexB.Rows.GetValueOrDefault(key);
            rows.Add(new ComparisonRow
            {
                NormalizedKey = key,
                Status = RowStatus.InvalidKey,
                KeyA = rowA is null ? null : Keys(rowA, dimCount),
                KeyB = rowB is null ? null : Keys(rowB, dimCount),
                Measures = [],
                Reason = $"Duplicate key after normalization (A: {Occurrences(countA, rowA is not null)}, " +
                         $"B: {Occurrences(countB, rowB is not null)} rows)",
            });
        }

        if (duplicateKeys.Count > 0)
            warnings.Add($"{duplicateKeys.Count} key(s) are duplicated after normalization and were not compared.");

        foreach (var key in indexA.Order)
        {
            if (duplicateKeys.Contains(key))
                continue;
            var rowA = indexA.Rows[key];
            if (indexB.Rows.TryGetValue(key, out var rowB))
            {
                var cells = new List<MeasureCell>(measurePairs.Count);
                for (var i = 0; i < measurePairs.Count; i++)
                    cells.Add(MeasureComparer.Compare(measurePairs[i].Alias, rowA[dimCount + i], rowB[dimCount + i], config));

                var mismatched = cells.Where(c => !c.IsMatch).Select(c => c.Alias).ToList();
                rows.Add(new ComparisonRow
                {
                    NormalizedKey = key,
                    Status = mismatched.Count == 0 ? RowStatus.Match : RowStatus.Mismatch,
                    KeyA = Keys(rowA, dimCount),
                    KeyB = Keys(rowB, dimCount),
                    Measures = cells,
                    Reason = mismatched.Count == 0 ? null : "Mismatch: " + string.Join(", ", mismatched),
                });
            }
            else
            {
                rows.Add(OneSided(key, rowA, dimCount, measurePairs, Side.A));
            }
        }

        foreach (var key in indexB.Order)
        {
            if (duplicateKeys.Contains(key) || indexA.Rows.ContainsKey(key))
                continue;
            rows.Add(OneSided(key, indexB.Rows[key], dimCount, measurePairs, Side.B));
        }

        return new EngineOutput
        {
            Rows = rows,
            Summary = Summarize(rows, a, b, measurePairs, dimCount),
            Warnings = warnings,
            HasDuplicateKeys = duplicateKeys.Count > 0,
        };
    }

    private static string Occurrences(int duplicates, bool present) => (duplicates + (present ? 1 : 0)).ToString();

    private static void EnsureShape(QueryResult result, int expectedColumns, Side side)
    {
        // An empty result may carry no column names (REST returns none when there are no rows).
        if (result.Rows.Count > 0 && result.Rows.Any(r => r.Length != expectedColumns))
            throw new DaxDiffException(ErrorCategory.InvalidQuery, "ENG-SHAPE",
                $"Query returned rows with a column count other than the {expectedColumns} expected.",
                "Check that every mapped field still exists and re-run the comparison.", side);
        if (result.Rows.Count > 0 && result.ColumnNames.Count != expectedColumns)
            throw new DaxDiffException(ErrorCategory.InvalidQuery, "ENG-SHAPE",
                $"Query returned {result.ColumnNames.Count} columns but {expectedColumns} were expected.",
                "Check that every mapped field still exists and re-run the comparison.", side);
    }

    private sealed class SideIndex
    {
        public Dictionary<string, object?[]> Rows { get; } = new(StringComparer.Ordinal);

        /// <summary>Key → number of extra occurrences beyond the first.</summary>
        public Dictionary<string, int> Duplicates { get; } = new(StringComparer.Ordinal);

        public List<string> Order { get; } = [];
    }

    private static SideIndex Index(QueryResult result, int dimCount, KeyNormalization policy)
    {
        var index = new SideIndex();
        foreach (var row in result.Rows)
        {
            var key = ValueCanonicalizer.CompositeKey(row.Take(dimCount).ToArray(), policy);
            if (index.Rows.TryAdd(key, row))
                index.Order.Add(key);
            else
                index.Duplicates[key] = index.Duplicates.GetValueOrDefault(key) + 1;
        }

        return index;
    }

    private static object?[] Keys(object?[] row, int dimCount) =>
        row.Take(dimCount).Select(v => v is DBNull ? null : v).ToArray();

    private static ComparisonRow OneSided(string key, object?[] row, int dimCount, IReadOnlyList<FieldPair> measures, Side side)
    {
        var cells = new List<MeasureCell>(measures.Count);
        for (var i = 0; i < measures.Count; i++)
        {
            var value = row[dimCount + i] is DBNull ? null : row[dimCount + i];
            cells.Add(new MeasureCell
            {
                Alias = measures[i].Alias,
                A = side == Side.A ? value : null,
                B = side == Side.B ? value : null,
                IsMatch = false,
                Reason = side == Side.A ? "Only in A" : "Only in B",
            });
        }

        var other = side == Side.A ? "B" : "A";
        return new ComparisonRow
        {
            NormalizedKey = key,
            Status = side == Side.A ? RowStatus.OnlyInA : RowStatus.OnlyInB,
            KeyA = side == Side.A ? Keys(row, dimCount) : null,
            KeyB = side == Side.B ? Keys(row, dimCount) : null,
            Measures = cells,
            Reason = $"Key not returned by {other} (member absent, filtered out, or all measures BLANK)",
        };
    }

    private static ComparisonSummary Summarize(
        List<ComparisonRow> rows, QueryResult a, QueryResult b, IReadOnlyList<FieldPair> measures, int dimCount)
    {
        var measureSummaries = new List<MeasureSummary>(measures.Count);
        for (var i = 0; i < measures.Count; i++)
        {
            var compared = 0;
            var mismatches = 0;
            decimal? maxDelta = null;
            foreach (var row in rows)
            {
                if (row.Status is not (RowStatus.Match or RowStatus.Mismatch))
                    continue;
                var cell = row.Measures[i];
                compared++;
                if (!cell.IsMatch)
                    mismatches++;
                if (cell.Delta is { } d && (maxDelta is null || Math.Abs(d) > maxDelta))
                    maxDelta = Math.Abs(d);
            }

            measureSummaries.Add(new MeasureSummary
            {
                Alias = measures[i].Alias,
                Compared = compared,
                Mismatches = mismatches,
                TotalA = Total(a, dimCount + i),
                TotalB = Total(b, dimCount + i),
                MaxAbsDelta = maxDelta,
            });
        }

        return new ComparisonSummary
        {
            RowsA = a.Rows.Count,
            RowsB = b.Rows.Count,
            TotalKeys = rows.Count,
            Matches = rows.Count(r => r.Status == RowStatus.Match),
            Mismatches = rows.Count(r => r.Status == RowStatus.Mismatch),
            OnlyInA = rows.Count(r => r.Status == RowStatus.OnlyInA),
            OnlyInB = rows.Count(r => r.Status == RowStatus.OnlyInB),
            InvalidKeys = rows.Count(r => r.Status == RowStatus.InvalidKey),
            Measures = measureSummaries,
        };
    }

    /// <summary>Sum of a numeric measure column over all retrieved rows; null when no numeric values.</summary>
    private static decimal? Total(QueryResult result, int column)
    {
        decimal? total = null;
        foreach (var row in result.Rows)
        {
            if (ValueCanonicalizer.TryToDecimal(row[column], out var d))
            {
                try
                {
                    total = (total ?? 0) + d;
                }
                catch (OverflowException)
                {
                    return null;
                }
            }
        }

        return total;
    }
}
