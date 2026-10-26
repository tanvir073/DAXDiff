using DaxDiff.Application;
using DaxDiff.Domain;
using DaxDiff.Tests.Unit.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class ComparisonRunnerTests
{
    private readonly ComparisonRunner _runner = new(NullLogger<ComparisonRunner>.Instance) { RetryBaseDelay = TimeSpan.FromMilliseconds(1) };

    private static RunRequest Request(FakeConnector a, FakeConnector b, ComparisonConfig? config = null) =>
        new(a, b, config ?? YearRegionSales(), ModelA(), ModelB());

    [Fact]
    public async Task AT01_EqualModels_CompleteWithFullMatch()
    {
        var data = Result(3, Row(2025L, "North", 10m), Row(2025L, "South", 20m));

        var result = await _runner.RunAsync(Request(FakeConnector.Returning("A", data), FakeConnector.Returning("B", data)), null, CancellationToken.None);

        Assert.Equal(RunStatus.Complete, result.Status);
        Assert.Equal(1m, result.Summary.MatchRate);
        Assert.Equal(2, result.Queries.Count);
        Assert.All(result.Queries, q => Assert.Equal(64, q.QueryHash.Length));
        Assert.Contains(result.Warnings, w => w.Contains("as observed by the current caller"));
    }

    [Fact]
    public async Task AT02_DifferentlyNamedMeasures_AreQueriedPerModel()
    {
        var data = Result(3, Row(2025L, "North", 10m));
        var a = FakeConnector.Returning("A", data);
        var b = FakeConnector.Returning("B", data);

        await _runner.RunAsync(Request(a, b), null, CancellationToken.None);

        Assert.Contains("[Total Sales]", a.ExecutedQueries.Single());
        Assert.Contains("[Sales Amount]", b.ExecutedQueries.Single());
    }

    [Fact]
    public async Task AT07_TruncatedResult_IsPartialNeverComplete()
    {
        var config = YearRegionSales() with { Limits = new ExecutionLimits { MaxRows = 2 } };
        var data = Result(3, Row(2025L, "N", 1m), Row(2025L, "S", 1m), Row(2025L, "E", 1m));

        var result = await _runner.RunAsync(Request(FakeConnector.Returning("A", data), FakeConnector.Returning("B", data), config), null, CancellationToken.None);

        Assert.Equal(RunStatus.Partial, result.Status);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, w => w.Contains("incomplete"));
        Assert.All(result.Queries, q => Assert.True(q.Truncated));
    }

    [Fact]
    public async Task AT08_PermissionFailure_IsFailedWithRemedy()
    {
        var denied = new FakeConnector("B", (_, _, _) => throw new InvalidOperationException("The user does not have permission to access the database."));

        var result = await _runner.RunAsync(Request(FakeConnector.Returning("A", Result(3)), denied), null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("Model B", result.FailureMessage);
        Assert.Contains("Build", result.FailureMessage);
        Assert.Contains("CON-PERMISSION", result.FailureMessage);
    }

    [Fact]
    public async Task AT10_Cancellation_ProducesCancelledResultWithoutRows()
    {
        using var cts = new CancellationTokenSource();
        var slow = new FakeConnector("A", async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Result(3);
        });
        var fast = FakeConnector.Returning("B", Result(3, Row(2025L, "N", 1m)));

        var run = _runner.RunAsync(Request(slow, fast), null, cts.Token);
        cts.CancelAfter(50);
        var result = await run;

        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task TransientErrors_AreRetried()
    {
        var calls = 0;
        var flaky = new FakeConnector("A", (_, _, _) =>
        {
            if (++calls < 2)
                throw new TimeoutException("timed out");
            return Task.FromResult(Result(3, Row(2025L, "N", 1m)));
        });

        var result = await _runner.RunAsync(Request(flaky, FakeConnector.Returning("B", Result(3, Row(2025L, "N", 1m)))), null, CancellationToken.None);

        Assert.Equal(RunStatus.Complete, result.Status);
        Assert.Equal(2, result.Queries.Single(q => q.Side == Side.A).Attempts);
    }

    [Fact]
    public async Task AT06_DuplicateKeys_FailByDefault_PartialWhenDiagnosed()
    {
        var a = Result(3, Row(2025L, "N", 1m), Row(2025L, "N", 2m));
        var b = Result(3, Row(2025L, "N", 1m));

        var failed = await _runner.RunAsync(Request(FakeConnector.Returning("A", a), FakeConnector.Returning("B", b)), null, CancellationToken.None);
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Contains(failed.Rows, r => r.Status == RowStatus.InvalidKey);

        var config = YearRegionSales() with { DuplicateKeyPolicy = DuplicateKeyPolicy.Diagnose };
        var diagnosed = await _runner.RunAsync(Request(FakeConnector.Returning("A", a), FakeConnector.Returning("B", b), config), null, CancellationToken.None);
        Assert.Equal(RunStatus.Partial, diagnosed.Status);
    }

    [Fact]
    public async Task InvalidConfiguration_FailsBeforeQuerying()
    {
        var a = FakeConnector.Returning("A", Result(3));
        var config = new ComparisonConfig { FieldPairs = [Dim("Year", Col("Date", "Year"))] };

        var result = await _runner.RunAsync(Request(a, FakeConnector.Returning("B", Result(3)), config), null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Empty(a.ExecutedQueries);
    }

    [Fact]
    public async Task AT09_DesktopVersusService_WarnsAboutSecurityContext()
    {
        var data = Result(3, Row(2025L, "N", 1m));
        var desktop = new FakeConnector("A", (_, _, _) => Task.FromResult(data), SourceKind.DesktopSession);

        var result = await _runner.RunAsync(Request(desktop, FakeConnector.Returning("B", data)), null, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Contains("Security contexts"));
    }

    [Fact]
    public async Task EstimateRows_ReadsCount()
    {
        var connector = FakeConnector.Returning("A", Result(1, Row(1234L)));
        var count = await _runner.EstimateRowsAsync(connector, YearRegionSales(), Side.A, ModelA(), CancellationToken.None);
        Assert.Equal(1234, count);
    }
}
