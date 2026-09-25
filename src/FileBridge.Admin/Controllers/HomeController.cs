using FileBridge.Admin.Models;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace FileBridge.Admin.Controllers;

[Authorize]
public sealed class HomeController(FileBridgeDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var vm = new DashboardViewModel
        {
            SucceededToday = await db.TransferHistories.CountAsync(h => h.StartedUtc >= todayUtc && h.TransferStatusId == TransferStatus.Succeeded),
            FailedToday = await db.TransferHistories.CountAsync(h => h.StartedUtc >= todayUtc && h.TransferStatusId == TransferStatus.Failed),
            InProgress = await db.TransferHistories.CountAsync(h => h.TransferStatusId == TransferStatus.InProgress),
            HeldQuarantine = await db.Quarantines.CountAsync(q => q.QuarantineStatusId == QuarantineStatus.Held),
            KillSwitchOn = await GlobalSettings.GetBoolAsync(db, SettingKeys.KillSwitch, default),
            Nodes = await db.NodeHeartbeats.AsNoTracking().OrderBy(n => n.NodeRole).ThenBy(n => n.NodeName)
                .Select(n => new ValueTuple<string, string, DateTime>(n.NodeName, n.NodeRole, n.LastSeenUtc)).ToListAsync()
        };

        var recentFailingRows = await db.TransferHistories.AsNoTracking()
            .Where(h => h.StartedUtc >= todayUtc && h.TransferStatusId == TransferStatus.Failed)
            .GroupBy(h => h.Job!.Name)
            .Select(g => new { JobName = g.Key, Failed = g.Count(), LastFailureUtc = g.Max(x => x.StartedUtc) })
            .OrderByDescending(x => x.Failed).Take(10).ToListAsync();
        vm.RecentFailingJobs = recentFailingRows.Select(r => (r.JobName, r.Failed, r.LastFailureUtc)).ToList();

        vm.RecentTransfers = await db.TransferHistories.AsNoTracking()
            .OrderByDescending(h => h.StartedUtc).Take(10)
            .Select(h => new RecentTransferRow(h.Id, h.Job!.Name, h.FileName, h.TransferStatusId, h.SizeBytes, h.StartedUtc, h.DurationMs))
            .ToListAsync();

        vm.ProcessSucceededToday = await db.ProcessRunHistories.CountAsync(h => h.StartedUtc >= todayUtc && h.StatusId == ProcessRunStatus.Succeeded);
        vm.ProcessFailedToday = await db.ProcessRunHistories.CountAsync(h => h.StartedUtc >= todayUtc &&
            (h.StatusId == ProcessRunStatus.Failed || h.StatusId == ProcessRunStatus.TimedOut));

        vm.RunningProcesses = await db.ProcessRunHistories.AsNoTracking()
            .Where(h => h.StatusId == ProcessRunStatus.Running)
            .OrderBy(h => h.StartedUtc)
            .Select(h => new RunningProcessRow(h.Id, h.ProcessJobId, h.ProcessJob!.Name, h.Pid, h.NodeName, h.TriggeredBy, h.StartedUtc))
            .ToListAsync();
        vm.ProcessesRunningNow = vm.RunningProcesses.Count;

        var recentFailingProcessRows = await db.ProcessRunHistories.AsNoTracking()
            .Where(h => h.StartedUtc >= todayUtc && (h.StatusId == ProcessRunStatus.Failed || h.StatusId == ProcessRunStatus.TimedOut))
            .GroupBy(h => h.ProcessJob!.Name)
            .Select(g => new { ProcessJobName = g.Key, Failed = g.Count(), LastFailureUtc = g.Max(x => x.StartedUtc) })
            .OrderByDescending(x => x.Failed).Take(10).ToListAsync();
        vm.RecentFailingProcessJobs = recentFailingProcessRows.Select(r => (r.ProcessJobName, r.Failed, r.LastFailureUtc)).ToList();

        vm.RecentProcessRuns = await db.ProcessRunHistories.AsNoTracking()
            .OrderByDescending(h => h.StartedUtc).Take(10)
            .Select(h => new RecentProcessRunRow(h.Id, h.ProcessJobId, h.ProcessJob!.Name, h.StatusId, h.ExitCode, h.StartedUtc, h.DurationMs))
            .ToListAsync();

        var upcomingTransfers = await BuildUpcomingJobsAsync();
        var upcomingBatch = await BuildUpcomingProcessJobsAsync();
        vm.UpcomingItems = upcomingTransfers.Select(u => new UpcomingItemRow(UpcomingKind.Transfer, u.JobId, u.JobName, u.ScheduleType, u.NextRunUtc))
            .Concat(upcomingBatch.Select(u => new UpcomingItemRow(UpcomingKind.BatchProcess, u.ProcessJobId, u.ProcessJobName, u.ScheduleType, u.NextRunUtc)))
            .OrderBy(u => u.NextRunUtc).Take(10).ToList();

        return View(vm);
    }

    // Approximated from each enabled job's own cron/interval schedule (the Admin tier never
    // reads the Worker's live Quartz cluster state -- see JobService/RequestService doc comments).
    private async Task<List<UpcomingJobRow>> BuildUpcomingJobsAsync()
    {
        var jobs = await db.Jobs.AsNoTracking().Where(j => j.IsEnabled && !j.IsPaused)
            .Select(j => new { j.Id, j.Name, j.ScheduleTypeId, j.CronExpression, j.IntervalSeconds, j.TimeZoneId })
            .ToListAsync();
        if (jobs.Count == 0) return [];

        var jobIds = jobs.Select(j => j.Id).ToList();
        var lastRuns = await db.TransferHistories.AsNoTracking()
            .Where(h => jobIds.Contains(h.JobId))
            .GroupBy(h => h.JobId)
            .Select(g => new { JobId = g.Key, LastStartedUtc = g.Max(x => x.StartedUtc) })
            .ToDictionaryAsync(x => x.JobId, x => x.LastStartedUtc);

        var now = DateTimeOffset.UtcNow;
        var upcoming = new List<UpcomingJobRow>();
        foreach (var j in jobs)
        {
            DateTime? next;
            if (j.ScheduleTypeId == ScheduleType.Cron && !string.IsNullOrWhiteSpace(j.CronExpression))
            {
                try
                {
                    var cron = new CronExpression(j.CronExpression) { TimeZone = ScheduleWindow.FindZone(j.TimeZoneId) };
                    next = cron.GetNextValidTimeAfter(now)?.UtcDateTime;
                }
                catch (FormatException)
                {
                    next = null; // invalid cron expression on this job; skip rather than guess
                }
            }
            else
            {
                var intervalSeconds = Math.Max(15, j.IntervalSeconds ?? 300);
                var baseline = lastRuns.TryGetValue(j.Id, out var last) ? last : now.UtcDateTime;
                next = baseline.AddSeconds(intervalSeconds);
            }
            if (next is not null) upcoming.Add(new UpcomingJobRow(j.Id, j.Name, j.ScheduleTypeId, next.Value));
        }
        return upcoming.OrderBy(u => u.NextRunUtc).Take(8).ToList();
    }

    // Mirrors BuildUpcomingJobsAsync but for ProcessJob/ProcessRunHistory.
    private async Task<List<UpcomingProcessJobRow>> BuildUpcomingProcessJobsAsync()
    {
        var jobs = await db.ProcessJobs.AsNoTracking().Where(j => j.IsEnabled && !j.IsPaused)
            .Select(j => new { j.Id, j.Name, j.ScheduleTypeId, j.CronExpression, j.IntervalSeconds, j.TimeZoneId })
            .ToListAsync();
        if (jobs.Count == 0) return [];

        var jobIds = jobs.Select(j => j.Id).ToList();
        var lastRuns = await db.ProcessRunHistories.AsNoTracking()
            .Where(h => jobIds.Contains(h.ProcessJobId))
            .GroupBy(h => h.ProcessJobId)
            .Select(g => new { ProcessJobId = g.Key, LastStartedUtc = g.Max(x => x.StartedUtc) })
            .ToDictionaryAsync(x => x.ProcessJobId, x => x.LastStartedUtc);

        var now = DateTimeOffset.UtcNow;
        var upcoming = new List<UpcomingProcessJobRow>();
        foreach (var j in jobs)
        {
            DateTime? next;
            if (j.ScheduleTypeId == ScheduleType.Cron && !string.IsNullOrWhiteSpace(j.CronExpression))
            {
                try
                {
                    var cron = new CronExpression(j.CronExpression) { TimeZone = ScheduleWindow.FindZone(j.TimeZoneId) };
                    next = cron.GetNextValidTimeAfter(now)?.UtcDateTime;
                }
                catch (FormatException)
                {
                    next = null; // invalid cron expression on this job; skip rather than guess
                }
            }
            else
            {
                var intervalSeconds = Math.Max(15, j.IntervalSeconds ?? 300);
                var baseline = lastRuns.TryGetValue(j.Id, out var last) ? last : now.UtcDateTime;
                next = baseline.AddSeconds(intervalSeconds);
            }
            if (next is not null) upcoming.Add(new UpcomingProcessJobRow(j.Id, j.Name, j.ScheduleTypeId, next.Value));
        }
        return upcoming.OrderBy(u => u.NextRunUtc).Take(8).ToList();
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> ToggleKillSwitch()
    {
        var on = await GlobalSettings.GetBoolAsync(db, SettingKeys.KillSwitch, default);
        var setting = await db.GlobalSettings.FindAsync(SettingKeys.KillSwitch) ?? throw new InvalidOperationException("Setting not seeded.");
        setting.SettingValue = (!on).ToString();
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
