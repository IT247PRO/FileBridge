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
[DisallowConcurrentExecution]
public sealed class ProcessWatchdogJob(FileBridgeDbContext db, ILogger<ProcessWatchdogJob> log) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var node = Environment.MachineName;

        var stuck = await db.ProcessRunHistories
            .Where(h => h.NodeName == node && h.StatusId == ProcessRunStatus.Running && h.CompletedUtc == null)
            .ToListAsync(ct);

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

        // Reconcile foreign nodes that have died/crashed without recovering their processes
        var tenMinutesAgo = DateTime.UtcNow.AddMinutes(-10);
        var activeNodes = (await db.NodeHeartbeats.AsNoTracking()
            .Where(n => n.NodeRole == "Worker" && n.LastSeenUtc >= tenMinutesAgo)
            .Select(n => n.NodeName).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var foreignDead = await db.ProcessRunHistories
            .Where(h => h.NodeName != node && h.StatusId == ProcessRunStatus.Running && h.CompletedUtc == null && h.StartedUtc < tenMinutesAgo)
            .ToListAsync(ct);

        foreach (var history in foreignDead)
        {
            if (!activeNodes.Contains(history.NodeName))
            {
                history.StatusId = ProcessRunStatus.Lost;
                history.ErrorMessage = $"Worker node '{history.NodeName}' appears to be offline (no heartbeat in >10m); outcome is unknown.";
                history.CompletedUtc = DateTime.UtcNow;
                log.LogWarning("Process run {RunId} marked Lost: owner node {OwnerNode} is offline", history.RunId, history.NodeName);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
