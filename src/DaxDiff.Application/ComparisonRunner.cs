using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DaxDiff.Comparison;
using DaxDiff.Connectors;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Application;

public sealed record RunRequest(
    IModelConnector ConnectorA,
    IModelConnector ConnectorB,
    ComparisonConfig Config,
    ModelMetadata? MetadataA,
    ModelMetadata? MetadataB,
    string ComparisonName = "",
    IReadOnlyList<ValidationIssue>? Issues = null)
{
    /// <summary>
    /// Builds a request from a saved comparison: compiles it by synonym and validates it, so pairing errors
    /// (unpaired keys, bad aggregations) block the run like any other validation error.
    /// </summary>
    public static RunRequest For(IModelConnector a, IModelConnector b, ComparisonDefinition definition, ModelMetadata? metadataA, ModelMetadata? metadataB)
    {
        var compiled = ComparisonCompiler.CompileAndValidate(definition, metadataA, metadataB);
        return new RunRequest(a, b, compiled.Config, metadataA, metadataB, definition.Name, compiled.Issues);
    }
}

public sealed record RunProgress(Side? Side, string Step, bool Done = false);

/// <summary>
/// Orchestrates one comparison run (FR-010): validates, queries both models in parallel with timeouts and
/// transient retries, compares, and decides Complete / Partial / Failed / Cancelled. Never throws for
/// expected failures; the outcome is always a <see cref="ComparisonResult"/>.
/// </summary>
public sealed class ComparisonRunner
{
    private readonly ILogger<ComparisonRunner> _logger;

    public ComparisonRunner(ILogger<ComparisonRunner> logger) => _logger = logger;

    public static string AppVersion { get; } =
        typeof(ComparisonRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Base delay between retries; exposed so tests can shorten it.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public async Task<ComparisonResult> RunAsync(RunRequest request, IProgress<RunProgress>? progress, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;
        var config = request.Config;
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId });

        ComparisonResult Finish(RunStatus status, string? failure = null, EngineOutput? output = null,
            IReadOnlyList<QueryAudit>? audits = null, IEnumerable<string>? warnings = null) => new()
        {
            RunId = runId,
            Status = status,
            ComparisonName = request.ComparisonName,
            SourceA = request.ConnectorA.Reference,
            SourceB = request.ConnectorB.Reference,
            Config = config,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            Rows = output?.Rows ?? [],
            Summary = output?.Summary ?? new ComparisonSummary(),
            Warnings = (warnings ?? []).ToList(),
            Queries = audits ?? [],
            SecurityContextA = request.ConnectorA.SecurityContext,
            SecurityContextB = request.ConnectorB.SecurityContext,
            FailureMessage = failure,
            AppVersion = AppVersion,
            MetadataA = request.MetadataA,
            MetadataB = request.MetadataB,
        };

        var issues = request.Issues ?? MappingValidator.Validate(config, request.MetadataA, request.MetadataB);
        if (MappingValidator.HasErrors(issues))
        {
            var message = string.Join(Environment.NewLine, issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message));
            _logger.LogWarning("Run {RunId} blocked by {Count} validation errors", runId, issues.Count(i => i.Severity == IssueSeverity.Error));
            return Finish(RunStatus.Failed, "The configuration is invalid:" + Environment.NewLine + message);
        }

        string daxA, daxB;
        try
        {
            daxA = DaxQueryBuilder.BuildComparisonQuery(config, Side.A, request.MetadataA);
            daxB = DaxQueryBuilder.BuildComparisonQuery(config, Side.B, request.MetadataB);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Finish(RunStatus.Failed, "Could not generate queries: " + ex.Message);
        }

        progress?.Report(new RunProgress(null, "Querying both models"));
        var taskA = ExecuteAsync(Side.A, request.ConnectorA, daxA, config.Options.Limits, progress, cancellationToken);
        var taskB = ExecuteAsync(Side.B, request.ConnectorB, daxB, config.Options.Limits, progress, cancellationToken);
        var outcomes = await Task.WhenAll(taskA, taskB);
        var audits = outcomes.Select(o => o.Audit).ToList();

        if (cancellationToken.IsCancellationRequested || outcomes.Any(o => o.Cancelled))
        {
            _logger.LogInformation("Run {RunId} cancelled", runId);
            return Finish(RunStatus.Cancelled, "The run was cancelled by the user.", audits: audits);
        }

        var failures = outcomes.Where(o => o.Error is not null).Select(o => o.Error!).ToList();
        if (failures.Count > 0)
            return Finish(RunStatus.Failed, string.Join(Environment.NewLine + Environment.NewLine, failures.Select(f => f.ToUserText())), audits: audits);

        progress?.Report(new RunProgress(null, "Comparing results"));
        EngineOutput output;
        try
        {
            output = ComparisonEngine.Compare(outcomes[0].Result!, outcomes[1].Result!, config);
        }
        catch (DaxDiffException ex)
        {
            return Finish(RunStatus.Failed, ex.ToUserText(), audits: audits);
        }

        var warnings = BuildWarnings(request, outcomes, output, issues);
        var status = RunStatus.Complete;
        string? failure = null;
        if (output.HasDuplicateKeys && config.Options.DuplicateKeyPolicy == DuplicateKeyPolicy.Fail)
        {
            status = RunStatus.Failed;
            failure = $"{output.Summary.InvalidKeys} key(s) are duplicated after normalization. Review the Invalid key rows, " +
                      "adjust key normalization or add dimensions, or switch the duplicate policy to Diagnose.";
        }
        else if (outcomes.Any(o => o.Result!.IsTruncated) || output.HasDuplicateKeys)
        {
            status = RunStatus.Partial;
        }

        progress?.Report(new RunProgress(null, "Finished", Done: true));
        _logger.LogInformation("Run {RunId} finished with status {Status}: {Keys} keys, {Mismatches} mismatches",
            runId, status, output.Summary.TotalKeys, output.Summary.Mismatches);
        return Finish(status, failure, output, audits, warnings);
    }

    /// <summary>Counts key tuples on one side before a full run (preview cardinality).</summary>
    public async Task<long?> EstimateRowsAsync(IModelConnector connector, ComparisonConfig config, Side side, ModelMetadata? metadata, CancellationToken cancellationToken)
    {
        var dax = DaxQueryBuilder.BuildRowEstimateQuery(config, side, metadata);
        var result = await connector.ExecuteDaxAsync(dax, new QueryOptions(1, TimeSpan.FromSeconds(config.Options.Limits.TimeoutSeconds)), cancellationToken);
        return result.Rows.Count == 1 && ValueCanonicalizer.TryToDecimal(result.Rows[0][0], out var count) ? (long)count : result.Rows.Count == 1 ? 0 : null;
    }

    public static string Hash(string dax) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(dax)));

    private sealed record SideOutcome(QueryAudit Audit, QueryResult? Result, DaxDiffException? Error, bool Cancelled);

    private async Task<SideOutcome> ExecuteAsync(
        Side side, IModelConnector connector, string dax, ExecutionLimits limits, IProgress<RunProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        var timeout = TimeSpan.FromSeconds(limits.TimeoutSeconds);

        QueryAudit Audit(QueryResult? result, string? error) => new()
        {
            Side = side,
            Transport = connector.Capabilities.TransportName,
            ModelIdentity = connector.Reference.Describe(),
            QueryHash = Hash(dax),
            Dax = dax,
            Duration = stopwatch.Elapsed,
            RowCount = result?.Rows.Count ?? 0,
            Attempts = attempts,
            Truncated = result?.IsTruncated ?? false,
            Error = error,
        };

        while (true)
        {
            attempts++;
            progress?.Report(new RunProgress(side, attempts == 1 ? "Running query" : $"Retrying (attempt {attempts})"));
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout + TimeSpan.FromSeconds(5));
            try
            {
                var result = await connector.ExecuteDaxAsync(dax, new QueryOptions(limits.MaxRows, timeout), attemptCts.Token);
                progress?.Report(new RunProgress(side, $"Retrieved {result.Rows.Count:N0} rows{(result.IsTruncated ? " (truncated)" : "")}", Done: true));
                _logger.LogInformation("Side {Side} query {Hash} returned {Rows} rows in {Ms} ms (truncated: {Truncated})",
                    side, Hash(dax)[..12], result.Rows.Count, stopwatch.ElapsedMilliseconds, result.IsTruncated);
                return new SideOutcome(Audit(result, null), result, null, false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new RunProgress(side, "Cancelled", Done: true));
                return new SideOutcome(Audit(null, "Cancelled"), null, null, true);
            }
            catch (Exception ex)
            {
                var error = ex is OperationCanceledException
                    ? new DaxDiffException(ErrorCategory.Timeout, "QRY-TIMEOUT", $"The query did not finish within {limits.TimeoutSeconds} seconds.",
                        "Increase the timeout, add filters or reduce the grain.", side, isTransient: true)
                    : ErrorClassifier.Classify(ex, connector.Capabilities.TransportName).WithSide(side);

                if (error.IsTransient && attempts <= limits.RetryCount)
                {
                    _logger.LogWarning("Side {Side} transient failure {Code}; retrying", side, error.DiagnosticCode);
                    try
                    {
                        await Task.Delay(RetryBaseDelay * Math.Pow(2, attempts - 1), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return new SideOutcome(Audit(null, "Cancelled"), null, null, true);
                    }

                    continue;
                }

                _logger.LogWarning("Side {Side} failed with {Code}", side, error.DiagnosticCode);
                progress?.Report(new RunProgress(side, "Failed", Done: true));
                return new SideOutcome(Audit(null, $"{error.DiagnosticCode}: {error.Message}"), null, error, false);
            }
        }
    }

    private static List<string> BuildWarnings(RunRequest request, SideOutcome[] outcomes, EngineOutput output, IReadOnlyList<ValidationIssue> issues)
    {
        var warnings = new List<string>
        {
            "Results are as observed by the current caller on each side. Row-level or object-level security can make rows " +
            "appear missing; missing keys are not necessarily global model differences.",
        };

        foreach (var (outcome, side) in outcomes.Zip([Side.A, Side.B]))
        {
            if (outcome.Result?.IsTruncated == true)
                warnings.Add($"Model {side} result is incomplete: {outcome.Result.TruncationReason} The run is Partial and must not be read as full equality.");
        }

        if (request.ConnectorA.Reference.Kind != request.ConnectorB.Reference.Kind ||
            request.ConnectorA.Reference.Kind == SourceKind.DesktopSession)
        {
            warnings.Add($"Security contexts differ or bypass RLS — A: {request.ConnectorA.SecurityContext}; B: {request.ConnectorB.SecurityContext}.");
        }

        if (request.MetadataA?.LastRefreshUtc is { } ra && request.MetadataB?.LastRefreshUtc is { } rb && ra != rb)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"Models were last refreshed at different times (A: {ra:yyyy-MM-dd HH:mm} UTC, B: {rb:yyyy-MM-dd HH:mm} UTC); data snapshots may be inconsistent."));
        }

        if (output.Summary.OnlyInA + output.Summary.OnlyInB > 0)
            warnings.Add("Keys missing on one side can mean the member is absent, filtered, hidden by security, or that every measure is BLANK for that key.");

        warnings.AddRange(output.Warnings);
        warnings.AddRange(issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.Message));
        return warnings;
    }
}
