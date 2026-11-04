using DaxDiff.Application;

namespace DaxDiff.Tests.Unit;

public class ProductLinksTests
{
    [Fact]
    public void BugReport_FillsVersionAndOs_Escaped()
    {
        var url = ProductLinks.NewBugReport("Desktop", "Microsoft Windows 10.0.26100 (X64)");

        Assert.StartsWith(ProductLinks.Repository + "/issues/new?template=bug_report.yml", url);
        Assert.Contains("&version=Desktop%20" + Uri.EscapeDataString(ComparisonRunner.AppVersion), url);
        Assert.Contains("&os=Microsoft%20Windows%2010.0.26100%20%28X64%29", url);
    }

    [Fact]
    public void BugReport_WithoutOs_LeavesTheFieldOut()
    {
        Assert.DoesNotContain("&os=", ProductLinks.NewBugReport("Web"));
    }

    /// <summary>GitHub only pre-fills fields whose id matches the query key, so the form must keep these ids.</summary>
    [Fact]
    public void BugReportForm_HasThePrefilledFieldIds()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DaxDiff.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var form = File.ReadAllText(Path.Combine(dir.FullName, ".github", "ISSUE_TEMPLATE", "bug_report.yml"));
        Assert.Contains("id: version", form);
        Assert.Contains("id: os", form);
    }
}
