using DaxDiff.Comparison;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class ComparisonCompilerTests
{
    private static ComparisonDefinition Definition(IReadOnlyList<ComparisonField> a, IReadOnlyList<ComparisonField> b) =>
        new() { Name = "Test", FieldsA = a, FieldsB = b };

    [Fact]
    public void FR018_FieldsPairBySynonym_KeysFirst_InModelAOrder()
    {
        var compiled = ComparisonCompiler.Compile(Definition(
            [
                Field(Measure("Sales", "Total Sales"), "Sales"),
                Field(Col("Date", "Year"), "Year", isKey: true),
                Field(Col("Geography", "Region"), "Region", isKey: true),
            ],
            [
                Field(Col("Geography", "Region"), " region ", isKey: true),
                Field(Measure("Fact", "Sales Amount"), "SALES"),
                Field(Col("Date", "Year"), "Year", isKey: true),
            ]), ModelA(), ModelB());

        Assert.False(compiled.HasErrors);
        Assert.Equal(["Year", "Region", "Sales"], compiled.Config.FieldPairs.Select(p => p.Alias));
        Assert.Equal([FieldRole.Key, FieldRole.Key, FieldRole.Value], compiled.Config.FieldPairs.Select(p => p.Role));
        Assert.Equal(Measure("Fact", "Sales Amount"), compiled.Config.Values.Single().Right);
    }

    [Fact]
    public void FR017_AggregationsAreCarriedPerSide()
    {
        var compiled = ComparisonCompiler.Compile(Definition(
            [Field(Col("Date", "Year"), isKey: true), Field(Col("Sales", "Amount"), "Amount", aggregation: Aggregation.Sum)],
            [Field(Col("Date", "Year"), isKey: true), Field(Col("Fact", "Amount"), "Amount", aggregation: Aggregation.Average)]),
            ModelA(), ModelB());

        var value = compiled.Config.Values.Single();
        Assert.Equal((Aggregation.Sum, Aggregation.Average), (value.LeftAggregation, value.RightAggregation));
        Assert.False(compiled.HasErrors);
    }

    [Fact]
    public void UnpairedKey_IsError_UnpairedValue_IsWarning()
    {
        var compiled = ComparisonCompiler.Compile(Definition(
            [Field(Col("Date", "Year"), isKey: true), Field(Measure("Sales", "Order Count"))],
            [Field(Col("Geography", "Region"), isKey: true)]));

        Assert.Contains(compiled.Issues, i => i.Code == "CMP-KEY-UNPAIRED" && i.Severity == IssueSeverity.Error && i.Alias == "Year");
        Assert.Contains(compiled.Issues, i => i.Code == "CMP-KEY-UNPAIRED" && i.Alias == "Region");
        Assert.Contains(compiled.Issues, i => i.Code == "CMP-UNPAIRED" && i.Severity == IssueSeverity.Warning);
        Assert.Empty(compiled.Config.FieldPairs);
    }

    [Fact]
    public void KeyOnOneSideOnly_IsError()
    {
        var compiled = ComparisonCompiler.Compile(Definition(
            [Field(Col("Date", "Year"), isKey: true)],
            [Field(Col("Date", "Year"), aggregation: Aggregation.Max)]));

        Assert.Contains(compiled.Issues, i => i.Code == "CMP-KEY-MISMATCH");
    }

    [Fact]
    public void DuplicateSynonymsAndFields_AreErrors()
    {
        var compiled = ComparisonCompiler.Compile(Definition(
            [Field(Col("Date", "Year"), "K", isKey: true), Field(Col("Geography", "Region"), "k", isKey: true), Field(Col("Date", "Year"), "Y2", isKey: true)],
            [Field(Col("Date", "Year"), "K", isKey: true)]));

        Assert.Contains(compiled.Issues, i => i.Code == "CMP-DUP-SYNONYM");
        Assert.Contains(compiled.Issues, i => i.Code == "CMP-DUP-FIELD");
    }

    [Fact]
    public void MeasureKey_AndBlankSynonym_AreErrors()
    {
        var compiled = ComparisonCompiler.Compile(Definition(
            [Field(Measure("Sales", "Total Sales"), isKey: true), Field(Col("Date", "Year"), " ")],
            []));

        Assert.Contains(compiled.Issues, i => i.Code == "CMP-MEASURE-KEY");
        Assert.Contains(compiled.Issues, i => i.Code == "CMP-NO-SYNONYM");
    }

    [Theory]
    [InlineData("Geography", "Region", Aggregation.Sum, true)] // text cannot be summed
    [InlineData("Geography", "Region", Aggregation.Concatenate, false)]
    [InlineData("Sales", "Amount", Aggregation.Concatenate, true)] // numbers are not concatenated
    [InlineData("Sales", "Amount", Aggregation.Average, false)]
    [InlineData("Date", "Date", Aggregation.Sum, true)]
    [InlineData("Date", "Date", Aggregation.Max, false)]
    public void FR017_AggregationMustSuitTheDataType(string table, string column, Aggregation aggregation, bool expectError)
    {
        var compiled = ComparisonCompiler.Compile(Definition([Field(Col(table, column), aggregation: aggregation)], []), ModelA());
        Assert.Equal(expectError, compiled.Issues.Any(i => i.Code == "CMP-AGGREGATION"));
    }

    [Fact]
    public void KeysOnlyAcrossTables_Warns()
    {
        var compiled = ComparisonCompiler.Compile(Definition(
            [Field(Col("Date", "Year"), isKey: true), Field(Col("Geography", "Region"), isKey: true)],
            [Field(Col("Date", "Year"), isKey: true), Field(Col("Geography", "Region"), isKey: true)]));

        Assert.False(compiled.HasErrors);
        Assert.Contains(compiled.Issues, i => i.Code == "CMP-KEYS-ONLY");
    }

    [Fact]
    public void Options_FlowIntoTheCompiledConfig()
    {
        var options = new ComparisonOptions { NullPolicy = NullPolicy.BlankEqualsZero };
        var compiled = ComparisonCompiler.Compile(new ComparisonDefinition { Options = options });
        Assert.Same(options, compiled.Config.Options);
        Assert.Contains(compiled.Issues, i => i.Code == "CMP-EMPTY");
    }

    [Theory]
    [InlineData(DataKind.Decimal, Aggregation.Sum)]
    [InlineData(DataKind.Text, Aggregation.Concatenate)]
    [InlineData(DataKind.DateTime, Aggregation.Max)]
    [InlineData(DataKind.Boolean, Aggregation.Count)]
    public void DefaultAggregation_DependsOnType(DataKind kind, Aggregation expected)
    {
        Assert.Equal(expected, AggregationRules.Default(FieldKind.Column, kind, isKey: false));
        Assert.Equal(Aggregation.None, AggregationRules.Default(FieldKind.Column, kind, isKey: true));
        Assert.Equal(Aggregation.None, AggregationRules.Default(FieldKind.Measure, kind, isKey: false));
    }
}
