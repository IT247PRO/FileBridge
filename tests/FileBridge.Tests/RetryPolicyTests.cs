using FileBridge.Core.Rules;
using Xunit;

namespace FileBridge.Tests;

public class RetryPolicyTests
{
    [Fact]
    public void BackoffGrowsExponentially()
    {
        var fixedRandom = new Random(42);
        var first = RetryPolicy.Backoff(1, 10, random: fixedRandom);
        var second = RetryPolicy.Backoff(2, 10, random: fixedRandom);
        var third = RetryPolicy.Backoff(3, 10, random: fixedRandom);
        Assert.True(second.TotalSeconds > first.TotalSeconds);
        Assert.True(third.TotalSeconds > second.TotalSeconds);
    }

    [Fact]
    public void BackoffRespectsCap()
    {
        var result = RetryPolicy.Backoff(20, 10, maxSeconds: 60, random: new Random(1));
        Assert.True(result.TotalSeconds <= 60 * 1.2);
    }
}
