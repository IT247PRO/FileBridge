using FileBridge.Core;
using FileBridge.Core.Rules;
using FileBridge.Infrastructure.Data;
using FileBridge.Infrastructure.Engine;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace FileBridge.Worker.Jobs;

/// <summary>
/// Runs one process job. Mirrors TransferQuartzJob's guard-clause shape (enabled/paused/blackout/active window)
/// before handing off to ProcessLaunchService, the process-job equivalent of TransferPipeline.
///
/// [DisallowConcurrentExecution] already stops Quartz itself from overlapping two Execute() calls for the same
/// process job (cluster-wide, via the persistent job store's locking) - but that guarantee only holds for as
/// long as this Worker process stays up. If the Worker crashes or is killed while the launched exe is still
/// running, the child process can be left running as an orphan on the OS while Quartz's own "currently
/// executing" tracking resets on recovery, so a later trigger fire would otherwise be free to launch a second
/// instance. The explicit "already running" check below closes that gap by asking the actual source of truth
/// (tblProcessRunHistory) instead of trusting Quartz's in-memory state, and it applies to manual Run Now too,
/// not just the schedule. It also falls back to the live OS process list (see
/// ProcessLaunchService.AdoptIfAlreadyRunningAsync) for the case tblProcessRunHistory alone can't cover: the
/// exe is already running but was never launched by FileBridge at all -- started by hand, or by a prior Worker
/// instance whose tracking row is gone.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ProcessLaunchQuartzJob(FileBridgeDbContext db, ProcessLaunchService launcher, ILogger<ProcessLaunchQuartzJob> log) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var map = context.MergedJobDataMap;
        var processJobId = int.Parse(map.GetString("processJobId")!);
        var triggeredBy = map.GetString("triggeredBy") ?? "schedule";
        var manual = map.GetBooleanValueFromString("manual");
        var ct = context.CancellationToken;

        var job = await db.ProcessJobs.AsNoTracking().Include(j => j.BlackoutWindows)
            .FirstOrDefaultAsync(j => j.Id == processJobId, ct);
        if (job is null) { log.LogWarning("Process job {ProcessJobId} no longer exists; skipping scheduled run", processJobId); return; }
        if (!job.IsEnabled) { log.LogInformation("Process job {ProcessJobId} is disabled; skipping", processJobId); return; }

        // Checks both tblProcessRunHistory and, if nothing's tracked, the live OS process list for this exe --
        // closes the gap where the exe is already running but FileBridge never recorded it (started by hand,
        // or by a prior Worker instance whose tracking row is gone). Either way, adopts it instead of guessing.
        if (await launcher.AdoptIfAlreadyRunningAsync(processJobId, ct))
        {
            log.LogInformation("Process job {ProcessJobId} already has a run in progress (tracked or just adopted); not starting another instance", processJobId);
            return;
        }

        if (!manual && job.IsPaused) { log.LogInformation("Process job {ProcessJobId} is paused; skipping scheduled run", processJobId); return; }

        if (ScheduleWindow.IsInBlackout(job.BlackoutWindows, DateTime.UtcNow))
        {
            log.LogInformation("Process job {ProcessJobId} is inside a blackout window; skipping", processJobId);
            return;
        }
        if (!manual)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ScheduleWindow.FindZone(job.TimeZoneId)).DateTime;
            if (!ScheduleWindow.IsWithinActiveWindow(job.ActiveDaysMask, job.ActiveFromTime, job.ActiveToTime, local))
            {
                log.LogDebug("Process job {ProcessJobId} is outside its active window; skipping", processJobId);
                return;
            }
        }

        await launcher.RunAsync(processJobId, triggeredBy, ct);
    }
}
