using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;
using Xunit;

namespace FileBridge.Tests;

public class FileFilterEvaluatorTests
{
    private static RemoteFile File(string path, long size = 100, int ageSeconds = 60) =>
        new(path, RemotePath.GetFileName(path), size, DateTimeOffset.UtcNow.AddSeconds(-ageSeconds));

    [Theory]
    [InlineData("*.csv", "in/data.csv", true)]
    [InlineData("*.csv", "in/data.CSV", true)]
    [InlineData("*.csv", "in/data.txt", false)]
    [InlineData("**/*.csv", "in/sub/data.csv", true)]
    [InlineData("data?.csv", "data1.csv", true)]
    [InlineData("data?.csv", "data12.csv", false)]
    public void GlobMatches(string pattern, string path, bool expected)
    {
        var filter = new FileFilter { Pattern = pattern };
        Assert.Equal(expected, FileFilterEvaluator.PatternMatches(filter, File(path)));
    }

    [Fact]
    public void ExcludeWinsOverInclude()
    {
        var filters = new[]
        {
            new FileFilter { Pattern = "*.csv" },
            new FileFilter { Pattern = "*_temp.csv", IsExclude = true }
        };
        Assert.True(FileFilterEvaluator.IsMatch(File("a.csv"), filters, DateTimeOffset.UtcNow));
        Assert.False(FileFilterEvaluator.IsMatch(File("a_temp.csv"), filters, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NoIncludesMeansEverythingPasses()
    {
        var filters = new[] { new FileFilter { Pattern = "*.tmp", IsExclude = true } };
        Assert.True(FileFilterEvaluator.IsMatch(File("anything.docx"), filters, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SizeAndAgeLimitsApply()
    {
        var filter = new FileFilter { Pattern = "*.csv", MinSizeBytes = 200, MinAgeSeconds = 120 };
        Assert.False(FileFilterEvaluator.IsMatch(File("a.csv", size: 100, ageSeconds: 200), new[] { filter }, DateTimeOffset.UtcNow));
        Assert.False(FileFilterEvaluator.IsMatch(File("a.csv", size: 300, ageSeconds: 10), new[] { filter }, DateTimeOffset.UtcNow));
        Assert.True(FileFilterEvaluator.IsMatch(File("a.csv", size: 300, ageSeconds: 200), new[] { filter }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InvalidRegexFailsValidation()
    {
        var filter = new FileFilter { Pattern = "[unterminated", IsRegex = true };
        Assert.False(FileFilterEvaluator.TryValidate(filter, out var error));
        Assert.NotNull(error);
    }
}
