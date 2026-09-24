namespace FileBridge.Core.Rules;

public static class RetryPolicy
{
    /// <summary>Exponential backoff (base * 2^(n-1)), capped, plus up to 20% jitter.</summary>
    public static TimeSpan Backoff(int attempt, int baseSeconds, int maxSeconds = 900, Random? random = null)
    {
        var exp = Math.Min(maxSeconds, baseSeconds * Math.Pow(2, Math.Max(0, attempt - 1)));
        var jitter = exp * 0.2 * (random ?? Random.Shared).NextDouble();
        return TimeSpan.FromSeconds(exp + jitter);
    }
}
