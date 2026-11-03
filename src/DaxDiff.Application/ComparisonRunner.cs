using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DaxDiff.Comparison;
using DaxDiff.Connectors;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;
using DaxDiff.Application.Resources;

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
            return Finish(RunStatus.Failed, Strings.Run_InvalidConfiguration + Environment.NewLine + message);
        }

        string daxA, daxB;
        try
        {
            daxA = DaxQueryBuilder.BuildComparisonQuery(config, Side.A, request.MetadataA);
            daxB = DaxQueryBuilder.BuildComparisonQuery(config, Side.B, request.MetadataB);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Finish(RunStatus.Failed, string.Format(CultureInfo.CurrentCulture, Strings.Run_QueryGenerationFailed, ex.Message));
        }

        progress?.Report(new RunProgress(null, Strings.Run_QueryingBoth));
        var taskA = ExecuteAsync(Side.A, request.ConnectorA, daxA, config.Options.Limits, progress, cancellationToken);
        var taskB = ExecuteAsync(Side.B, request.ConnectorB, daxB, config.Options.Limits, progress, cancellationToken);
        var outcomes = await Task.WhenAll(taskA, taskB);
        var audits = outcomes.Select(o => o.Audit).ToList();

        if (cancellationToken.IsCancellationRequested || outcomes.Any(o => o.Cancelled))
        {
            _logger.LogInformation("Run {RunId} cancelled", runId);
            return Finish(RunStatus.Cancelled, Strings.Run_CancelledByUser, audits: audits);
        }

        var failures = outcomes.Where(o => o.Error is not null).Select(o => o.Error!).ToList();
        if (failures.Count > 0)
            return Finish(RunStatus.Failed, string.Join(Environment.NewLine + Environment.NewLine, failures.Select(f => f.ToUserText())), audits: audits);

        progress?.Report(new RunProgress(null, Strings.Run_Comparing));
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
            failure = string.Format(CultureInfo.CurrentCulture, Strings.Run_DuplicateKeysFailure, output.Summary.InvalidKeys);
        }
        else if (outcomes.Any(o => o.Result!.IsTruncated) || output.HasDuplicateKeys)
        {
            status = RunStatus.Partial;
        }

        progress?.Report(new RunProgress(null, Strings.Run_Finished, Done: true));
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
            progress?.Report(new RunProgress(side, attempts == 1 ? Strings.Run_RunningQuery : string.Format(CultureInfo.CurrentCulture, Strings.Run_Retrying, attempts)));
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout + TimeSpan.FromSeconds(5));
            try
            {
                var result = await connector.ExecuteDaxAsync(dax, new QueryOptions(limits.MaxRows, timeout), attemptCts.Token);
                progress?.Report(new RunProgress(side, string.Format(CultureInfo.CurrentCulture, Strings.Run_RetrievedRows, result.Rows.Count) + (result.IsTruncated ? Strings.Run_TruncatedSuffix : ""), Done: true));
                _logger.LogInformation("Side {Side} query {Hash} returned {Rows} rows in {Ms} ms (truncated: {Truncated})",
                    side, Hash(dax)[..12], result.Rows.Count, stopwatch.ElapsedMilliseconds, result.IsTruncated);
                return new SideOutcome(Audit(result, null), result, null, false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new RunProgress(side, Strings.Run_Cancelled, Done: true));
                return new SideOutcome(Audit(null, Strings.Run_Cancelled), null, null, true);
            }
            catch (Exception ex)
            {
                var error = ex is OperationCanceledException
                    ? new DaxDiffException(ErrorCategory.Timeout, "QRY-TIMEOUT", string.Format(CultureInfo.CurrentCulture, Strings.Run_Timeout, limits.TimeoutSeconds),
                        Strings.Run_TimeoutRemedy, side, isTransient: true)
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
                        return new SideOutcome(Audit(null, Strings.Run_Cancelled), null, null, true);
                    }

                    continue;
                }

                _logger.LogWarning("Side {Side} failed with {Code}", side, error.DiagnosticCode);
                progress?.Report(new RunProgress(side, Strings.Run_Failed, Done: true));
                return new SideOutcome(Audit(null, $"{error.DiagnosticCode}: {error.Message}"), null, error, false);
            }
        }
    }

    private static List<string> BuildWarnings(RunRequest request, SideOutcome[] outcomes, EngineOutput output, IReadOnlyList<ValidationIssue> issues)
    {
        var warnings = new List<string>
        {
            Strings.Run_SecurityScope,
        };

        foreach (var (outcome, side) in outcomes.Zip([Side.A, Side.B]))
        {
            if (outcome.Result?.IsTruncated == true)
                warnings.Add(string.Format(CultureInfo.CurrentCulture, Strings.Run_SideIncomplete, side, outcome.Result.TruncationReason));
        }

        if (request.ConnectorA.Reference.Kind != request.ConnectorB.Reference.Kind ||
            request.ConnectorA.Reference.Kind == SourceKind.DesktopSession)
        {
            warnings.Add(string.Format(CultureInfo.CurrentCulture, Strings.Run_SecurityContexts, request.ConnectorA.SecurityContext, request.ConnectorB.SecurityContext));
        }

        if (request.MetadataA?.LastRefreshUtc is { } ra && request.MetadataB?.LastRefreshUtc is { } rb && ra != rb)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, Strings.Run_RefreshTimes, ra, rb));
        }

        if (output.Summary.OnlyInA + output.Summary.OnlyInB > 0)
            warnings.Add(Strings.Run_MissingKeys);

        warnings.AddRange(output.Warnings);
        warnings.AddRange(issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.Message));
        return warnings;
    }
}
