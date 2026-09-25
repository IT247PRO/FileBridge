using System.Diagnostics;
using FileBridge.Core;
using FileBridge.Core.Rules;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace FileBridge.Worker.Jobs;

/// <summary>
/// The "wait for exit" state for a running process job lives only in the awaited Process object inside
/// ProcessLaunchService.RunAsync's Quartz job execution, in memory. If this Worker process restarts mid-run,
/// that await is lost and the row would stay Running forever with nothing watching it. This job reconciles
/// that: it only looks at rows this node itself spawned, and marks any whose PID/start-time no longer
/// resolves as Lost rather than guessing an outcome.
/// </summary>
public sealed class ProcessWatchdogJob(FileBridgeDbContext db, ILogger<ProcessWatchdogJob> log) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var node = Environment.MachineName;

        var stuck = await db.ProcessRunHistories
            .Where(h => h.NodeName == node && h.StatusId == ProcessRunStatus.Running && h.CompletedUtc == null)
            .ToListAsync(ct);
        if (stuck.Count == 0) return;

        foreach (var history in stuck)
        {
            if (history.Pid is not { } pid || history.ProcessStartTimeUtc is not { } startedUtc) continue;

            bool stillOurs;
            try { stillOurs = ProcessIdentity.StartTimeMatches(startedUtc, Process.GetProcessById(pid).StartTime.ToUniversalTime()); }
            catch (ArgumentException) { stillOurs = false; }

            if (!stillOurs)
            {
                history.StatusId = ProcessRunStatus.Lost;
                history.ErrorMessage = "This node restarted while the process was running; its outcome is unknown.";
                history.CompletedUtc = DateTime.UtcNow;
                log.LogWarning("Process run {RunId} (process job {ProcessJobId}, PID {Pid}) marked Lost: no longer resolvable after a restart",
                    history.RunId, history.ProcessJobId, pid);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
