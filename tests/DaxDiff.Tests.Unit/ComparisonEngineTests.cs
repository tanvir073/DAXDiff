using DaxDiff.Comparison;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class ComparisonEngineTests
{
    private static readonly ComparisonConfig Config = YearRegionSales();

    [Fact]
    public void AT01_EqualResults_AllMatch()
    {
        var a = Result(3, Row(2025L, "North", 1200m), Row(2025L, "South", 850m));
        var b = Result(3, Row(2025L, "North", 1200m), Row(2025L, "South", 850m));

        var output = ComparisonEngine.Compare(a, b, Config);

        Assert.All(output.Rows, r => Assert.Equal(RowStatus.Match, r.Status));
        Assert.Equal(2, output.Summary.Matches);
        Assert.Equal(1m, output.Summary.MatchRate);
        Assert.False(output.HasDuplicateKeys);
    }

    [Fact]
    public void WorkedExample_4_5_ClassifiesEveryCase()
    {
        var a = Result(3, Row(2025L, "North", 1200m), Row(2025L, "South", 850m), Row(2025L, "East", 300m));
        var b = Result(3, Row(2025L, "North", 1198m), Row(2025L, "South", 850m), Row(2025L, "West", 150m));

        var output = ComparisonEngine.Compare(a, b, Config);
        var byRegion = output.Rows.ToDictionary(r => (string)r.DisplayKey[1]!);

        Assert.Equal(RowStatus.Mismatch, byRegion["North"].Status);
        Assert.Equal(2m, byRegion["North"].Measures[0].Delta);
        Assert.Equal(RowStatus.Match, byRegion["South"].Status);
        Assert.Equal(0m, byRegion["South"].Measures[0].Delta);
        Assert.Equal(RowStatus.OnlyInA, byRegion["East"].Status);
        Assert.Equal(300m, byRegion["East"].Measures[0].A);
        Assert.Null(byRegion["East"].Measures[0].Delta);
        Assert.Equal(RowStatus.OnlyInB, byRegion["West"].Status);
        Assert.Equal(150m, byRegion["West"].Measures[0].B);

        Assert.Equal(4, output.Summary.TotalKeys);
        Assert.Equal(1, output.Summary.Mismatches);
        Assert.Equal(1, output.Summary.OnlyInA);
        Assert.Equal(1, output.Summary.OnlyInB);
        Assert.Equal(2350m, output.Summary.Measures[0].TotalA);
        Assert.Equal(2198m, output.Summary.Measures[0].TotalB);
        Assert.Equal(1, output.Summary.Measures[0].Mismatches);
    }

    [Fact]
    public void AT03_MismatchedMembers_FlaggedOnlyAAndOnlyB()
    {
        var a = Result(3, Row(2025L, "North", 1m));
        var b = Result(3, Row(2025L, "Norht", 1m));

        var output = ComparisonEngine.Compare(a, b, Config);

        Assert.Contains(output.Rows, r => r.Status == RowStatus.OnlyInA && (string)r.KeyA![1]! == "North");
        Assert.Contains(output.Rows, r => r.Status == RowStatus.OnlyInB && (string)r.KeyB![1]! == "Norht");
    }

    [Theory]
    [InlineData(100.4, true)]
    [InlineData(100.6, false)]
    public void AT04_AbsoluteTolerance(double bValue, bool expectMatch)
    {
        var config = Config.With(o => o with { Tolerance = new ToleranceSettings { Mode = ToleranceMode.Tolerance, Absolute = 0.5m } });
        var a = Result(3, Row(2025L, "North", 100m));
        var b = Result(3, Row(2025L, "North", bValue));

        var row = ComparisonEngine.Compare(a, b, config).Rows.Single();

        Assert.Equal(expectMatch ? RowStatus.Match : RowStatus.Mismatch, row.Status);
    }

    [Fact]
    public void AT04_RelativeTolerance_UsesLargerMagnitude()
    {
        var config = Config.With(o => o with { Tolerance = new ToleranceSettings { Mode = ToleranceMode.Tolerance, Relative = 0.01m } });
        var a = Result(3, Row(2025L, "North", 1000m), Row(2025L, "South", 1000m));
        var b = Result(3, Row(2025L, "North", 1010m), Row(2025L, "South", 1011m));

        var rows = ComparisonEngine.Compare(a, b, config).Rows;

        Assert.Equal(RowStatus.Match, rows[0].Status); // 10 ≤ 0.01 × 1010
        Assert.Equal(RowStatus.Mismatch, rows[1].Status); // 11 > 0.01 × 1011
    }

    [Theory]
    [InlineData(NullPolicy.Strict, RowStatus.Mismatch)]
    [InlineData(NullPolicy.BlankEqualsZero, RowStatus.Match)]
    public void AT05_BlankVersusZero_FollowsNullPolicy(NullPolicy policy, RowStatus expected)
    {
        var config = Config.With(o => o with { NullPolicy = policy });
        var a = Result(3, Row(2025L, "North", null));
        var b = Result(3, Row(2025L, "North", 0m));

        var row = ComparisonEngine.Compare(a, b, config).Rows.Single();

        Assert.Equal(expected, row.Status);
    }

    [Fact]
    public void BlankOnBothSides_IsMatch()
    {
        var row = ComparisonEngine.Compare(Result(3, Row(2025L, "N", null)), Result(3, Row(2025L, "N", DBNull.Value)), Config).Rows.Single();
        Assert.Equal(RowStatus.Match, row.Status);
    }

    [Fact]
    public void AT06_DuplicateKeysAfterNormalization_AreReportedNotDeduplicated()
    {
        var config = Config.With(o => o with { KeyNormalization = new KeyNormalization { CaseInsensitive = true } });
        var a = Result(3, Row(2025L, "North", 1m), Row(2025L, "NORTH", 2m), Row(2025L, "South", 3m));
        var b = Result(3, Row(2025L, "north", 1m), Row(2025L, "South", 3m));

        var output = ComparisonEngine.Compare(a, b, config);

        Assert.True(output.HasDuplicateKeys);
        var invalid = Assert.Single(output.Rows, r => r.Status == RowStatus.InvalidKey);
        Assert.Contains("A: 2", invalid.Reason);
        Assert.Contains("B: 1", invalid.Reason);
        Assert.DoesNotContain(output.Rows, r => r.Status != RowStatus.InvalidKey && r.NormalizedKey == invalid.NormalizedKey);
        Assert.Equal(1, output.Summary.Matches);
    }

    [Fact]
    public void TextIdentifiers_AreNotCastToNumbers()
    {
        var config = new ComparisonConfig
        {
            FieldPairs = [Key("Code", Col("Product", "Code")), Value("Qty", Measure("S", "Qty"), Measure("S", "Qty"))],
        };
        var a = Result(2, Row("0012", 1L));
        var b = Result(2, Row("12", 1L));

        var output = ComparisonEngine.Compare(a, b, config);

        Assert.Equal(1, output.Summary.OnlyInA);
        Assert.Equal(1, output.Summary.OnlyInB);
    }

    [Fact]
    public void NumericKeys_CompareAcrossClrTypesAndScale()
    {
        var a = Result(3, Row(2025L, "N", 1.20m));
        var b = Result(3, Row(2025m, "N", 1.2d));

        var row = ComparisonEngine.Compare(a, b, Config).Rows.Single();

        Assert.Equal(RowStatus.Match, row.Status);
    }

    [Fact]
    public void KeyNormalization_IsExplicit()
    {
        var a = Result(3, Row(2025L, " North ", 1m));
        var b = Result(3, Row(2025L, "north", 1m));

        Assert.Equal(0, ComparisonEngine.Compare(a, b, Config).Summary.Matches);

        var normalized = Config.With(o => o with { KeyNormalization = new KeyNormalization { TrimWhitespace = true, CaseInsensitive = true } });
        var row = ComparisonEngine.Compare(a, b, normalized).Rows.Single();
        Assert.Equal(RowStatus.Match, row.Status);
        Assert.Equal(" North ", row.KeyA![1]);
        Assert.Equal("north", row.KeyB![1]);
    }

    [Fact]
    public void UnicodeNormalization_MatchesComposedAndDecomposedForms()
    {
        var composed = "Malmö";
        var decomposed = "Malmö";
        var config = Config.With(o => o with { KeyNormalization = new KeyNormalization { Unicode = UnicodeNormalization.FormC } });

        Assert.Equal(0, ComparisonEngine.Compare(Result(3, Row(1L, composed, 1m)), Result(3, Row(1L, decomposed, 1m)), Config).Summary.Matches);
        Assert.Equal(1, ComparisonEngine.Compare(Result(3, Row(1L, composed, 1m)), Result(3, Row(1L, decomposed, 1m)), config).Summary.Matches);
    }

    [Fact]
    public void BlankAndEmptyKeys_AreDistinctUnlessConfigured()
    {
        var a = Result(3, Row(1L, null, 1m));
        var b = Result(3, Row(1L, "", 1m));

        Assert.Equal(0, ComparisonEngine.Compare(a, b, Config).Summary.Matches);
        var config = Config.With(o => o with { KeyNormalization = new KeyNormalization { BlankEqualsEmpty = true } });
        Assert.Equal(1, ComparisonEngine.Compare(a, b, config).Summary.Matches);
    }

    [Fact]
    public void DateOnlyPolicy_IgnoresTimeOfDay()
    {
        var config = new ComparisonConfig
        {
            FieldPairs = [Key("Date", Col("Date", "Date")), Value("V", Measure("S", "V"), Measure("S", "V"))],
        };
        var a = Result(2, Row(new DateTime(2025, 1, 1), 1L));
        var b = Result(2, Row(new DateTime(2025, 1, 1, 12, 0, 0), 1L));

        Assert.Equal(0, ComparisonEngine.Compare(a, b, config).Summary.Matches);
        var dateOnly = config.With(o => o with { KeyNormalization = new KeyNormalization { DateOnly = true } });
        Assert.Equal(1, ComparisonEngine.Compare(a, b, dateOnly).Summary.Matches);
    }

    [Fact]
    public void CompositeKeys_DoNotCollideOnSeparators()
    {
        var a = Result(3, Row(1L, "a|b", 1m));
        var b = Result(3, Row(1L, "a", 1m));

        var output = ComparisonEngine.Compare(a, b, Config);

        Assert.Equal(0, output.Summary.Matches);
    }

    [Fact]
    public void GrandTotalGrain_WithoutDimensions_ComparesSingleRow()
    {
        var config = new ComparisonConfig { FieldPairs = [Value("Sales", Measure("S", "Sales"), Measure("S", "Sales"))] };

        var output = ComparisonEngine.Compare(Result(1, Row(10m)), Result(1, Row(11m)), config);

        Assert.Equal(RowStatus.Mismatch, output.Rows.Single().Status);
    }

    [Fact]
    public void WrongColumnCount_Throws()
    {
        var ex = Assert.Throws<DaxDiffException>(() => ComparisonEngine.Compare(Result(2, Row(1L, 1m)), Result(3, Row(1L, "x", 1m)), Config));
        Assert.Equal(Side.A, ex.Side);
    }

    [Fact]
    public void EmptyResults_WithoutColumnNames_AreAccepted()
    {
        var empty = new QueryResult { ColumnNames = [], Rows = [] };
        var output = ComparisonEngine.Compare(empty, Result(3, Row(1L, "N", 1m)), Config);
        Assert.Equal(1, output.Summary.OnlyInB);
    }

    [Fact]
    public void KeysOnly_ComparesMemberExistence()
    {
        var config = new ComparisonConfig { FieldPairs = [Key("Customer", Col("Customer", "Id"))] };

        var output = ComparisonEngine.Compare(Result(1, Row("C1"), Row("C2")), Result(1, Row("C2"), Row("C3")), config);

        Assert.Equal((1, 1, 1), (output.Summary.Matches, output.Summary.OnlyInA, output.Summary.OnlyInB));
        Assert.All(output.Rows, r => Assert.Empty(r.Measures));
    }

    private static (QueryResult A, QueryResult B) FourCases() =>
    (
        Result(3, Row(2025L, "North", 1200m), Row(2025L, "South", 850m), Row(2025L, "East", 300m), Row(2025L, "Mid", 10m)),
        Result(3, Row(2025L, "North", 1198m), Row(2025L, "South", 850m), Row(2025L, "West", 150m), Row(2025L, "Mid", 10m))
    );

    [Fact]
    public void ResultSettings_DefaultKeepsEveryRow()
    {
        var (a, b) = FourCases();

        var output = ComparisonEngine.Compare(a, b, Config);

        Assert.Equal(5, output.Rows.Count);
        Assert.Equal(0, output.Summary.RowsOmitted);
    }

    [Fact]
    public void ResultSettings_DifferencesOnly_DropsMatchesButCountsThem()
    {
        var (a, b) = FourCases();

        var output = ComparisonEngine.Compare(a, b, Config.With(o => o with { Results = new ResultSettings { Rows = ResultRows.DifferencesOnly } }));

        Assert.Equal(3, output.Rows.Count);
        Assert.DoesNotContain(output.Rows, r => r.Status == RowStatus.Match);
        Assert.Equal(2, output.Summary.Matches);
        Assert.Equal(2, output.Summary.RowsOmitted);
        Assert.Contains(output.Warnings, w => w.Contains("3 of 5"));
    }

    [Fact]
    public void ResultSettings_RowLimit_KeepsDifferencesFirstInOriginalOrder()
    {
        var (a, b) = FourCases();
        var all = ComparisonEngine.Compare(a, b, Config).Rows;

        var output = ComparisonEngine.Compare(a, b, Config.With(o => o with { Results = new ResultSettings { MaxRows = 4 } }));

        Assert.Equal(4, output.Rows.Count);
        Assert.Equal(3, output.Rows.Count(r => r.Status != RowStatus.Match));
        var kept = output.Rows.Select(r => r.NormalizedKey).ToList();
        Assert.Equal(all.Select(r => r.NormalizedKey).Where(kept.Contains), kept);
        Assert.Equal(1, output.Summary.RowsOmitted);
        Assert.Equal(5, output.Summary.TotalKeys);
    }
}
