using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl.Matchers;

namespace FileBridge.Worker.Jobs;

/// <summary>
/// Reconciles tblProcessJob against Quartz's own store every 30s, exactly like ScheduleSyncJob does for
/// tblJob/TransferQuartzJob, just against the "process" group and ProcessLaunchQuartzJob.
/// </summary>
public sealed class ProcessScheduleSyncJob(FileBridgeDbContext db, ISchedulerFactory schedulerFactory, ILogger<ProcessScheduleSyncJob> log) : IJob
{
    private const string Group = "process";

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var scheduler = await schedulerFactory.GetScheduler(ct);

        var jobs = await db.ProcessJobs.AsNoTracking().Where(j => j.IsEnabled).ToListAsync(ct);
        var liveIds = jobs.Select(j => j.Id).ToHashSet();

        foreach (var job in jobs)
        {
            var jobKey = new JobKey($"process-{job.Id}", Group);
            var triggerKey = new TriggerKey($"process-{job.Id}", Group);
            var existingTrigger = await scheduler.GetTrigger(triggerKey, ct);
            var currentVersion = existingTrigger?.JobDataMap.GetString("version");

            if (existingTrigger is not null && currentVersion == job.Version.ToString()) continue;

            var jobDetail = JobBuilder.Create<ProcessLaunchQuartzJob>()
                .WithIdentity(jobKey)
                .UsingJobData("processJobId", job.Id.ToString())
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

            log.LogInformation("Scheduled process job {ProcessJobId} ({Name}), version {Version}", job.Id, job.Name, job.Version);
        }

        var quartzJobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(Group), ct);
        foreach (var key in quartzJobKeys)
        {
            if (!key.Name.StartsWith("process-", StringComparison.Ordinal)) continue;
            var id = int.Parse(key.Name["process-".Length..]);
            if (!liveIds.Contains(id))
            {
                await scheduler.DeleteJob(key, ct);
                log.LogInformation("Removed schedule for process job {ProcessJobId} (deleted or disabled)", id);
            }
        }
    }
}
