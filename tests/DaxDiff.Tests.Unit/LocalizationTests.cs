using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DaxDiff.Comparison;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

/// <summary>English and Swedish text: every resource is translated, with the same placeholders.</summary>
public partial class LocalizationTests
{
    public static TheoryData<string> NeutralResourceFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.resx", SearchOption.AllDirectories)
                     .Where(f => !f.EndsWith(".sv.resx", StringComparison.OrdinalIgnoreCase) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .Order())
            data.Add(Path.GetRelativePath(RepoRoot(), file));
        return data;
    }

    [Theory]
    [MemberData(nameof(NeutralResourceFiles))]
    public void EveryTextHasASwedishTranslationWithTheSamePlaceholders(string neutral)
    {
        var english = Read(Path.Combine(RepoRoot(), neutral));
        var swedish = Read(Path.Combine(RepoRoot(), neutral[..^".resx".Length] + ".sv.resx"));

        Assert.Equal(english.Keys.Order(), swedish.Keys.Order());
        foreach (var (key, text) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(swedish[key]), $"{key} has no Swedish text");
            Assert.True(Placeholders(text).SetEquals(Placeholders(swedish[key])), $"{key}: placeholders differ between English and Swedish");
        }
    }

    [Fact]
    public void SwedishUiCulture_GivesSwedishMessages()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("sv-SE");
            var issues = MappingValidator.Validate(new ComparisonConfig(), null, null);
            Assert.Contains(issues, i => i.Code == "MAP-EMPTY" && !i.Message.Contains("Nothing", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void FilterPopUpMessages_SayThisFilterInsteadOfANumber()
    {
        var filter = new FilterPair { LeftColumn = Col("Geography", "Region"), RightColumn = Col("Geo", "Region") };

        var numbered = MappingValidator.ValidateFilter(filter, 3, ModelA(), ModelB());
        var single = MappingValidator.ValidateFilter(filter, ModelA(), ModelB());

        Assert.Contains(numbered, i => i.Code == "FLT-EMPTY" && i.Message == "Filter 3 has no values selected.");
        Assert.Contains(single, i => i.Code == "FLT-EMPTY" && i.Message == "This filter has no values selected.");
    }

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static HashSet<string> Placeholders(string text) => [.. PlaceholderPattern().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"\{\d+(,-?\d+)?(:[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DaxDiff.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (DaxDiff.sln) not found.");
    }
}
