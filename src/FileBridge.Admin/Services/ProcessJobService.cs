using System.Text.Json;
using FileBridge.Admin.Models;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Services;

public sealed class ProcessJobService(FileBridgeDbContext db, ISecretProtector secrets, ICurrentUser user)
{
    public async Task<List<ProcessJob>> ListAsync() =>
        await db.ProcessJobs.AsNoTracking().OrderBy(j => j.Name).ToListAsync();

    public async Task<ProcessJobEditModel?> GetForEditAsync(int id)
    {
        var job = await db.ProcessJobs.AsNoTracking().AsSplitQuery()
            .Include(j => j.EnvironmentVariables).Include(j => j.NotificationRules)
            .FirstOrDefaultAsync(j => j.Id == id);
        if (job is null) return null;

        return new ProcessJobEditModel
        {
            Id = job.Id, Name = job.Name, Description = job.Description,
            IsEnabled = job.IsEnabled, IsPaused = job.IsPaused,
            ScheduleTypeId = job.ScheduleTypeId, CronExpression = job.CronExpression, IntervalSeconds = job.IntervalSeconds,
            TimeZoneId = job.TimeZoneId, ActiveFromTime = job.ActiveFromTime, ActiveToTime = job.ActiveToTime,
            ActiveDays = FileBridge.Core.Rules.ScheduleWindow.FromMask(job.ActiveDaysMask).ToList(),
            ExecutablePath = job.ExecutablePath, Arguments = job.Arguments, WorkingDirectory = job.WorkingDirectory,
            TimeoutSeconds = job.TimeoutSeconds, SuccessExitCodesCsv = job.SuccessExitCodesCsv,
            Version = job.Version, RowVersion = job.RowVersion,
            // Secret values are write-only: never sent back to the browser once saved (same convention as Endpoint's Password).
            EnvironmentVariables = job.EnvironmentVariables.Select(e => new ProcessEnvVarModel
            { Id = e.Id, Key = e.Key, IsSecret = e.IsSecret, Value = e.IsSecret ? null : e.Value }).ToList(),
            Notifications = job.NotificationRules.Select(n => new NotificationModel
            { Id = n.Id, NotificationEventId = n.NotificationEventId, NotificationChannelId = n.NotificationChannelId, Target = n.Target, IsEnabled = n.IsEnabled }).ToList()
        };
    }

    public IReadOnlyList<string> Validate(ProcessJobEditModel m)
    {
        var errors = new List<string>();
        if (m.ScheduleTypeId == ScheduleType.Cron && string.IsNullOrWhiteSpace(m.CronExpression)) errors.Add("A cron expression is required for a cron schedule.");
        if (m.ScheduleTypeId == ScheduleType.Interval && (m.IntervalSeconds is null or < 15)) errors.Add("Interval must be at least 15 seconds.");
        try { TimeZoneInfo.FindSystemTimeZoneById(m.TimeZoneId); } catch { errors.Add($"'{m.TimeZoneId}' is not a recognized time zone."); }
        if (m.TimeoutSeconds is < 1) errors.Add("Timeout, if set, must be at least 1 second.");

        var codes = (m.SuccessExitCodesCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (codes.Length == 0 || codes.Any(c => !int.TryParse(c, out _)))
            errors.Add("Success exit codes must be a comma-separated list of whole numbers, e.g. \"0\" or \"0,3010\".");

        var keys = m.EnvironmentVariables.Select(e => e.Key?.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();
        if (keys.Count != keys.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            errors.Add("Environment variable names must be unique.");

        return errors;
    }

    /// <summary>If approval is required, stages the change in tblChangeRequest instead of applying it.</summary>
    public async Task<(bool Applied, string Message)> SubmitAsync(ProcessJobEditModel m)
    {
        var errors = Validate(m);
        if (errors.Count > 0) return (false, string.Join(" ", errors));

        if (await GlobalSettings.GetBoolAsync(db, SettingKeys.RequireApproval, default))
        {
            db.ChangeRequests.Add(new ChangeRequest
            {
                EntityName = "ProcessJob",
                EntityKey = m.Id == 0 ? null : m.Id.ToString(),
                PayloadJson = JsonSerializer.Serialize(m),
                ApprovalStatusId = ApprovalStatus.Pending,
                RequestedBy = user.Name,
                RequestedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return (false, "Change submitted for approval.");
        }

        await ApplyAsync(m);
        return (true, "Batch process job saved.");
    }

    public async Task ApproveAsync(long changeRequestId)
    {
        var cr = await db.ChangeRequests.FindAsync(changeRequestId) ?? throw new InvalidOperationException("Change request not found.");
        if (cr.EntityName != "ProcessJob") throw new NotSupportedException($"Approval for entity '{cr.EntityName}' is not implemented.");
        var model = JsonSerializer.Deserialize<ProcessJobEditModel>(cr.PayloadJson)!;
        await ApplyAsync(model);
        cr.ApprovalStatusId = ApprovalStatus.Approved;
        cr.ReviewedBy = user.Name;
        cr.ReviewedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ApplyAsync(ProcessJobEditModel m)
    {
        var job = m.Id == 0 ? new ProcessJob() : await db.ProcessJobs.AsSplitQuery()
            .Include(j => j.EnvironmentVariables).Include(j => j.NotificationRules)
            .FirstAsync(j => j.Id == m.Id);

        if (m.Id != 0 && m.RowVersion is not null) db.Entry(job).Property(x => x.RowVersion).OriginalValue = m.RowVersion;

        job.Name = m.Name; job.Description = m.Description;
        job.IsEnabled = m.IsEnabled; job.IsPaused = m.IsPaused;
        job.ScheduleTypeId = m.ScheduleTypeId; job.CronExpression = m.CronExpression; job.IntervalSeconds = m.IntervalSeconds;
        job.TimeZoneId = m.TimeZoneId; job.ActiveFromTime = m.ActiveFromTime; job.ActiveToTime = m.ActiveToTime;
        job.ActiveDaysMask = FileBridge.Core.Rules.ScheduleWindow.ToMask(m.ActiveDays);
        job.ExecutablePath = m.ExecutablePath; job.Arguments = m.Arguments; job.WorkingDirectory = m.WorkingDirectory;
        job.TimeoutSeconds = m.TimeoutSeconds; job.SuccessExitCodesCsv = m.SuccessExitCodesCsv;
        job.Version++;

        EntitySync.Sync(job.EnvironmentVariables, m.EnvironmentVariables, (e, s) =>
        {
            e.Key = s.Key; e.IsSecret = s.IsSecret;
            if (s.IsSecret)
            {
                // Blank Value means "keep the existing protected value" (same convention as Endpoint's Password).
                if (!string.IsNullOrWhiteSpace(s.Value)) e.Value = secrets.Protect(s.Value) ?? "";
            }
            else e.Value = s.Value ?? "";
        }, s => new ProcessEnvironmentVariable());

        // ProcessJobId is left unset by the factory below (same as JobService does for JobFolderMap etc.): EF's
        // relationship fixup sets it from the job.NotificationRules navigation at SaveChanges time, which also
        // correctly handles a brand-new job whose Id doesn't exist yet at this point.
        EntitySync.Sync(job.NotificationRules, m.Notifications, (e, s) =>
        { e.NotificationEventId = s.NotificationEventId; e.NotificationChannelId = s.NotificationChannelId; e.Target = s.Target; e.IsEnabled = s.IsEnabled; },
            s => new NotificationRule());

        if (job.Id == 0) db.ProcessJobs.Add(job);
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(int processJobId)
    {
        var hasHistory = await db.ProcessRunHistories.AnyAsync(h => h.ProcessJobId == processJobId);
        var job = await db.ProcessJobs.FindAsync(processJobId) ?? throw new InvalidOperationException("Process job not found.");
        if (hasHistory) { job.IsEnabled = false; job.IsPaused = true; await db.SaveChangesAsync(); return false; }
        db.ProcessJobs.Remove(job);
        await db.SaveChangesAsync();
        return true;
    }
}
