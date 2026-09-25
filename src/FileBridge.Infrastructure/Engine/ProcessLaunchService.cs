using System.Diagnostics;
using System.Text;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileBridge.Infrastructure.Engine;

/// <summary>
/// Launches a ProcessJob's executable and tracks it by PID, mirroring TransferPipeline's shape for the
/// file-transfer path: one instance per run (scoped), a dedicated DbContext for the long-lived history row,
/// and a finally block that always finalizes history/notifications even on cancellation or timeout.
/// </summary>
public sealed class ProcessLaunchService(
    FileBridgeDbContext db, IDbContextFactory<FileBridgeDbContext> dbFactory,
    ISecretProtector secrets, INotifier notifier, ILogger<ProcessLaunchService> log)
{
    private const int TailCharLimit = 8000;
    private readonly string _node = Environment.MachineName;

    public async Task RunAsync(int processJobId, string triggeredBy, CancellationToken ct)
    {
        using var activity = Telemetry.Source.StartActivity("process.run");
        activity?.SetTag("filebridge.process_job_id", processJobId);

        if (await GlobalSettings.GetBoolAsync(db, SettingKeys.KillSwitch, ct))
        {
            log.LogWarning("Kill switch is on; skipping process job {ProcessJobId}", processJobId);
            return;
        }

        var job = await db.ProcessJobs.AsNoTracking().Include(j => j.EnvironmentVariables)
            .FirstOrDefaultAsync(j => j.Id == processJobId, ct)
            ?? throw new NonRetryableException($"Process job {processJobId} was not found.");

        var runId = Guid.NewGuid();
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ScheduleWindow.FindZone(job.TimeZoneId));

        // Own DbContext for the history row: the process can run a long time and this method's injected db
        // is also used for the kill-switch check above / KillAsync below, same reasoning as TransferPipeline's
        // per-file context.
        await using var historyDb = await dbFactory.CreateDbContextAsync(ct);
        var history = new ProcessRunHistory
        {
            ProcessJobId = job.Id,
            RunId = runId,
            TriggeredBy = triggeredBy,
            NodeName = _node,
            StatusId = ProcessRunStatus.Starting,
            StartedUtc = DateTime.UtcNow
        };
        historyDb.ProcessRunHistories.Add(history);
        await historyDb.SaveChangesAsync(ct);

        var psi = new ProcessStartInfo
        {
            FileName = job.ExecutablePath,
            Arguments = ProcessArgTemplater.Apply(job.Arguments, job.Name, nowLocal),
            WorkingDirectory = string.IsNullOrWhiteSpace(job.WorkingDirectory) ? "" : job.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var ev in job.EnvironmentVariables)
            psi.Environment[ev.Key] = ev.IsSecret ? secrets.Unprotect(ev.Value) ?? "" : ev.Value;

        using var process = new Process { StartInfo = psi };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendCapped(stdOut, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendCapped(stdErr, e.Data); };

        var finalStatus = ProcessRunStatus.Failed;
        int? exitCode = null;
        string? error = null;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            history.Pid = process.Id;
            history.ProcessStartTimeUtc = process.StartTime.ToUniversalTime();
            history.StatusId = ProcessRunStatus.Running;
            await historyDb.SaveChangesAsync(ct);

            log.LogInformation("Started process job {ProcessJobId} ({Name}) as PID {Pid}, triggered by {TriggeredBy}",
                job.Id, job.Name, process.Id, triggeredBy);

            using var timeoutCts = job.TimeoutSeconds is { } t ? new CancellationTokenSource(TimeSpan.FromSeconds(t)) : null;
            using var linked = timeoutCts is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var waitCt = linked?.Token ?? ct;

            try
            {
                await process.WaitForExitAsync(waitCt);
                exitCode = process.ExitCode;
                finalStatus = ParseExitCodes(job.SuccessExitCodesCsv).Contains(exitCode.Value) ? ProcessRunStatus.Succeeded : ProcessRunStatus.Failed;
            }
            catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
            {
                finalStatus = ProcessRunStatus.TimedOut;
                error = $"Timed out after {job.TimeoutSeconds}s; the process (tree) was killed.";
                TryKillTree(process, log);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            finalStatus = ProcessRunStatus.Failed;
            log.LogWarning(ex, "Failed to launch process job {ProcessJobId} ({Name})", job.Id, job.Name);
        }
        finally
        {
            history.StatusId = finalStatus;
            history.ExitCode = exitCode;
            history.ErrorMessage = error;
            history.CompletedUtc = DateTime.UtcNow;
            history.DurationMs = (long)(history.CompletedUtc.Value - history.StartedUtc).TotalMilliseconds;
            history.StdOutTail = stdOut.Length == 0 ? null : stdOut.ToString();
            history.StdErrTail = stdErr.Length == 0 ? null : stdErr.ToString();
            await historyDb.SaveChangesAsync(CancellationToken.None);

            Telemetry.Processes.Add(1, new KeyValuePair<string, object?>("job", job.Name), new("status", finalStatus.ToString()));
            log.LogInformation("Process job {ProcessJobId} finished: {Status} (exit code {ExitCode})", job.Id, finalStatus, exitCode);

            if (finalStatus is ProcessRunStatus.Failed or ProcessRunStatus.TimedOut)
                await notifier.NotifyAsync(NotificationEvent.JobFailed, null, $"FileBridge: {job.Name} failed",
                    $"Run {runId}: {finalStatus} (exit code {(exitCode?.ToString() ?? "n/a")}). {error}", CancellationToken.None, processJobId: job.Id);
            else if (finalStatus == ProcessRunStatus.Succeeded)
                await notifier.NotifyAsync(NotificationEvent.JobSucceeded, null, $"FileBridge: {job.Name} completed",
                    $"Run {runId}: exit code {exitCode}.", CancellationToken.None, processJobId: job.Id);
        }
    }

    /// <summary>Only succeeds on the node that spawned the process: Process.Kill(pid) requires the local machine.</summary>
    public async Task KillAsync(int processJobId, string requestedBy, CancellationToken ct)
    {
        var history = await db.ProcessRunHistories
            .Where(h => h.ProcessJobId == processJobId && h.StatusId == ProcessRunStatus.Running)
            .OrderByDescending(h => h.StartedUtc)
            .FirstOrDefaultAsync(ct)
            ?? throw new NonRetryableException("This process job is not currently running.");

        if (history.NodeName != _node)
            throw new NonRetryableException($"This process is running on node '{history.NodeName}'; the kill request must be handled there.");
        if (history.Pid is not { } pid)
            throw new NonRetryableException("No PID was recorded for this run.");

        Process proc;
        try { proc = Process.GetProcessById(pid); }
        catch (ArgumentException)
        {
            history.StatusId = ProcessRunStatus.Lost;
            history.ErrorMessage = "The process was no longer running when the kill request was processed.";
            history.CompletedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        // PID-reuse guard: only kill it if the start time we recorded still matches the live process.
        if (history.ProcessStartTimeUtc is not { } startedUtc || !ProcessIdentity.StartTimeMatches(startedUtc, proc.StartTime.ToUniversalTime()))
            throw new NonRetryableException("The PID no longer matches the process this job started (likely reused by the OS); refusing to kill it.");

        proc.Kill(entireProcessTree: true);
        log.LogInformation("Killed process job {ProcessJobId} PID {Pid}, requested by {RequestedBy}", processJobId, pid, requestedBy);
        // history is finalized by the awaited WaitForExitAsync back in RunAsync, not here.
    }

    private static void AppendCapped(StringBuilder sb, string line)
    {
        if (sb.Length >= TailCharLimit) return;
        sb.AppendLine(line);
        if (sb.Length > TailCharLimit) sb.Length = TailCharLimit;
    }

    private static HashSet<int> ParseExitCodes(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(s => int.TryParse(s, out var i) ? i : (int?)null)
           .Where(i => i.HasValue).Select(i => i!.Value).ToHashSet();

    private static void TryKillTree(Process process, ILogger log)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) { log.LogWarning(ex, "Failed to kill timed-out process tree for PID {Pid}", process.Id); }
    }
}
