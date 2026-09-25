namespace FileBridge.Core.Rules;

/// <summary>
/// Windows reuses PIDs, so a stored Pid alone never safely identifies "the process we started" - it's paired
/// with the process's own StartTime. That pairing has to tolerate a small gap rather than demand exact equality:
/// SQL Server's DATETIME2(3) truncates the value we stored to millisecond precision, while a live
/// Process.StartTime read back from the OS carries full precision, so an exact `==` comparison fails on every
/// single still-running process, not just ones that were genuinely replaced.
/// </summary>
public static class ProcessIdentity
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    public static bool StartTimeMatches(DateTime storedUtc, DateTime liveUtc) => (liveUtc - storedUtc).Duration() <= Tolerance;
}
