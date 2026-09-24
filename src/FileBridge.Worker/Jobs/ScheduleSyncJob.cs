using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl.Matchers;

namespace FileBridge.Worker.Jobs;

/// <summary>
/// Reconciles tblJob against Quartz's own store every 30s: schedules new/changed jobs, removes deleted/disabled
/// ones. The trigger's "version" data value is compared to tblJob.Version so an edit reschedules automatically.
/// </summary>
public sealed class ScheduleSyncJob(FileBridgeDbContext db, ISchedulerFactory schedulerFactory, ILogger<ScheduleSyncJob> log) : IJob
{
    private const string Group = "transfer";

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var scheduler = await schedulerFactory.GetScheduler(ct);

        var jobs = await db.Jobs.AsNoTracking().Where(j => j.IsEnabled).ToListAsync(ct);
        var liveIds = jobs.Select(j => j.Id).ToHashSet();

        foreach (var job in jobs)
        {
            var jobKey = new JobKey($"transfer-{job.Id}", Group);
            var triggerKey = new TriggerKey($"transfer-{job.Id}", Group);
            var existingTrigger = await scheduler.GetTrigger(triggerKey, ct);
            var currentVersion = existingTrigger?.JobDataMap.GetString("version");

            if (existingTrigger is not null && currentVersion == job.Version.ToString()) continue;

            var jobDetail = JobBuilder.Create<TransferQuartzJob>()
                .WithIdentity(jobKey)
                .UsingJobData("jobId", job.Id.ToString())
                .UsingJobData("triggeredBy", "schedule")
                .UsingJobData("manual", "false")
                .StoreDurably()
                .Build();

            ITrigger trigger;
            var tz = FileBridge.Core.Rules.ScheduleWindow.FindZone(job.TimeZoneId);
            if (job.ScheduleTypeId == FileBridge.Core.ScheduleType.Cron && !string.IsNullOrWhiteSpace(job.CronExpression))
            {
                trigger = TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .ForJob(jobKey)
                    .WithCronSchedule(job.CronExpression, x => x.InTimeZone(tz))
                    .UsingJobData("version", job.Version.ToString())
                    .Build();
            }
            else
            {
                var seconds = Math.Max(15, job.IntervalSeconds ?? 300);
                trigger = TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .ForJob(jobKey)
                    .WithSimpleSchedule(x => x.WithIntervalInSeconds(seconds).RepeatForever())
                    .UsingJobData("version", job.Version.ToString())
                    .Build();
            }

            if (await scheduler.CheckExists(jobKey, ct))
                await scheduler.RescheduleJob(triggerKey, trigger, ct);
            else
                await scheduler.ScheduleJob(jobDetail, trigger, ct);

            log.LogInformation("Scheduled job {JobId} ({Name}), version {Version}", job.Id, job.Name, job.Version);
        }

        // Remove Quartz jobs whose tblJob row was deleted or disabled.
        var quartzJobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(Group), ct);
        foreach (var key in quartzJobKeys)
        {
            if (!key.Name.StartsWith("transfer-", StringComparison.Ordinal)) continue;
            var id = int.Parse(key.Name["transfer-".Length..]);
            if (!liveIds.Contains(id))
            {
                await scheduler.DeleteJob(key, ct);
                log.LogInformation("Removed schedule for job {JobId} (deleted or disabled)", id);
            }
        }
    }
}
