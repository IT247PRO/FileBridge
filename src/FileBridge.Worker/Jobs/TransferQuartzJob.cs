using FileBridge.Core.Rules;
using FileBridge.Infrastructure.Data;
using FileBridge.Infrastructure.Engine;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace FileBridge.Worker.Jobs;

/// <summary>
/// Runs one job. DataMap carries "jobId" and "triggeredBy" as strings (Quartz UseProperties requires this).
/// A "manual" DataMap flag (set by RunRequestPollerJob / RunNow) bypasses the pause flag and active window,
/// same as a human clicking Run Now should.
/// </summary>
[DisallowConcurrentExecution]
public sealed class TransferQuartzJob(FileBridgeDbContext db, TransferPipeline pipeline, ILogger<TransferQuartzJob> log) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var map = context.MergedJobDataMap;
        var jobId = int.Parse(map.GetString("jobId")!);
        var triggeredBy = map.GetString("triggeredBy") ?? "schedule";
        var manual = map.GetBooleanValueFromString("manual");
        var ct = context.CancellationToken;

        var job = await db.Jobs.AsNoTracking().Include(j => j.BlackoutWindows)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) { log.LogWarning("Job {JobId} no longer exists; skipping scheduled run", jobId); return; }
        if (!job.IsEnabled) { log.LogInformation("Job {JobId} is disabled; skipping", jobId); return; }
        if (!manual && job.IsPaused) { log.LogInformation("Job {JobId} is paused; skipping scheduled run", jobId); return; }

        if (ScheduleWindow.IsInBlackout(job.BlackoutWindows, DateTime.UtcNow))
        {
            log.LogInformation("Job {JobId} is inside a blackout window; skipping", jobId);
            return;
        }
        if (!manual)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ScheduleWindow.FindZone(job.TimeZoneId)).DateTime;
            if (!ScheduleWindow.IsWithinActiveWindow(job, local))
            {
                log.LogDebug("Job {JobId} is outside its active window; skipping", jobId);
                return;
            }
        }

        log.LogInformation("Starting job {JobId} ({Name}), triggered by {TriggeredBy}", jobId, job.Name, triggeredBy);
        var summary = await pipeline.RunAsync(jobId, triggeredBy, dryRun: false, ct);
        log.LogInformation("Job {JobId} finished: {Succeeded} succeeded, {Failed} failed, {Quarantined} quarantined, {Skipped} skipped",
            jobId, summary.Succeeded, summary.Failed, summary.Quarantined, summary.Skipped);
    }
}
