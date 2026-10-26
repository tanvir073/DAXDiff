namespace DaxDiff.Domain;

/// <summary>Raw tabular result of one DAX query, with ordinal columns.</summary>
public sealed class QueryResult
{
    public required IReadOnlyList<string> ColumnNames { get; init; }
    public required IReadOnlyList<object?[]> Rows { get; init; }

    /// <summary>True when the transport or row cap cut the result short (NFR-05).</summary>
    public bool IsTruncated { get; init; }

    public string? TruncationReason { get; init; }
}

public enum RowStatus
{
    Match,
    Mismatch,
    OnlyInA,
    OnlyInB,

    /// <summary>Invalid or ambiguous key, e.g. duplicate after normalization (AT-06).</summary>
    InvalidKey,
}

public enum RunStatus
{
    Complete,
    Partial,
    Failed,
    Cancelled,
}

public sealed record MeasureCell
{
    public required string Alias { get; init; }
    public object? A { get; init; }
    public object? B { get; init; }

    /// <summary>Signed A − B, numeric measures only.</summary>
    public decimal? Delta { get; init; }

    /// <summary>Percentage difference; null means N/A (zero denominator).</summary>
    public decimal? DeltaPercent { get; init; }

    public bool IsMatch { get; init; }
    public string? Reason { get; init; }
}

public sealed record ComparisonRow
{
    public required string NormalizedKey { get; init; }
    public required RowStatus Status { get; init; }

    /// <summary>Original key values from A, preserved for audit (§4.2).</summary>
    public object?[]? KeyA { get; init; }

    public object?[]? KeyB { get; init; }
    public required IReadOnlyList<MeasureCell> Measures { get; init; }
    public string? Reason { get; init; }

    /// <summary>Display key components, preferring A.</summary>
    public object?[] DisplayKey => KeyA ?? KeyB ?? [];
}

public sealed record MeasureSummary
{
    public required string Alias { get; init; }
    public int Compared { get; init; }
    public int Mismatches { get; init; }
    public decimal? TotalA { get; init; }
    public decimal? TotalB { get; init; }
    public decimal? MaxAbsDelta { get; init; }
}

public sealed record ComparisonSummary
{
    public int RowsA { get; init; }
    public int RowsB { get; init; }
    public int TotalKeys { get; init; }
    public int Matches { get; init; }
    public int Mismatches { get; init; }
    public int OnlyInA { get; init; }
    public int OnlyInB { get; init; }
    public int InvalidKeys { get; init; }
    public IReadOnlyList<MeasureSummary> Measures { get; init; } = [];

    /// <summary>Share of keys present on both sides that fully match.</summary>
    public decimal MatchRate => TotalKeys == 0 ? 0 : Math.Round((decimal)Matches / TotalKeys, 6);
}

public sealed record QueryAudit
{
    public required Side Side { get; init; }
    public required string Transport { get; init; }
    public required string ModelIdentity { get; init; }

    /// <summary>SHA-256 of the executed DAX, so queries can be identified without logging literals.</summary>
    public required string QueryHash { get; init; }

    public required string Dax { get; init; }
    public TimeSpan Duration { get; init; }
    public int RowCount { get; init; }
    public int Attempts { get; init; }
    public bool Truncated { get; init; }
    public string? Error { get; init; }
}

/// <summary>Outcome of one comparison run (FR-010, FR-012).</summary>
public sealed record ComparisonResult
{
    public required Guid RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required ModelReference SourceA { get; init; }
    public required ModelReference SourceB { get; init; }
    public required ComparisonConfig Config { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset FinishedUtc { get; init; }
    public IReadOnlyList<ComparisonRow> Rows { get; init; } = [];
    public ComparisonSummary Summary { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<QueryAudit> Queries { get; init; } = [];
    public string SecurityContextA { get; init; } = "";
    public string SecurityContextB { get; init; } = "";
    public string? FailureMessage { get; init; }
    public string AppVersion { get; init; } = "";

    /// <summary>Metadata used for the run, for field types and refresh times in the audit trail.</summary>
    public ModelMetadata? MetadataA { get; init; }

    public ModelMetadata? MetadataB { get; init; }

    /// <summary>True only when every query completed in full and no key was invalid.</summary>
    public bool IsComplete => Status == RunStatus.Complete;
}
