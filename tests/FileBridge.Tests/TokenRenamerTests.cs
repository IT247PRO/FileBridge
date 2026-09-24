using FileBridge.Core.Rules;
using Xunit;

namespace FileBridge.Tests;

public class TokenRenamerTests
{
    [Fact]
    public void ExpandsFilenameTokens()
    {
        var result = TokenRenamer.Apply("{name}_archived{ext}", "report.csv", "MyJob", DateTimeOffset.UtcNow);
        Assert.Equal("report_archived.csv", result);
    }

    [Fact]
    public void ExpandsDateTokenInLocalTime()
    {
        var when = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var result = TokenRenamer.Apply("{filename}_{date:yyyyMMdd}", "x.csv", "Job", when);
        Assert.Equal("x.csv_20260304", result);
    }

    [Fact]
    public void ExpandsJobAndGuidTokens()
    {
        var result = TokenRenamer.Apply("{job}_{guid}", "x.csv", "My Job!", DateTimeOffset.UtcNow);
        Assert.StartsWith("My_Job_", result.Replace("!", ""));
    }

    [Fact]
    public void EmptyPatternReturnsOriginalName()
    {
        Assert.Equal("x.csv", TokenRenamer.Apply(null, "x.csv", "Job", DateTimeOffset.UtcNow));
        Assert.Equal("x.csv", TokenRenamer.Apply("", "x.csv", "Job", DateTimeOffset.UtcNow));
    }
}
