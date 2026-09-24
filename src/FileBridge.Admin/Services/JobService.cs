using System.Text.Json;
using FileBridge.Admin.Models;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Services;

public sealed class JobService(FileBridgeDbContext db, ICurrentUser user)
{
    public async Task<List<Job>> ListAsync() =>
        await db.Jobs.AsNoTracking().Include(j => j.SourceEndpoint).Include(j => j.DestinationEndpoint)
            .OrderBy(j => j.Name).ToListAsync();

    public async Task<JobEditModel?> GetForEditAsync(int id)
    {
        var job = await db.Jobs.AsNoTracking().AsSplitQuery()
            .Include(j => j.FolderMaps).Include(j => j.Filters).Include(j => j.SemaphoreRule)
            .Include(j => j.PostAction).Include(j => j.NotificationRules).Include(j => j.SlaRules)
            .FirstOrDefaultAsync(j => j.Id == id);
        if (job is null) return null;

        return new JobEditModel
        {
            Id = job.Id, Name = job.Name, Description = job.Description,
            TransferDirectionId = job.TransferDirectionId,
            SourceEndpointId = job.SourceEndpointId, DestinationEndpointId = job.DestinationEndpointId,
            IsEnabled = job.IsEnabled, IsPaused = job.IsPaused,
            ScheduleTypeId = job.ScheduleTypeId, CronExpression = job.CronExpression, IntervalSeconds = job.IntervalSeconds,
            TimeZoneId = job.TimeZoneId, ActiveFromTime = job.ActiveFromTime, ActiveToTime = job.ActiveToTime,
            ActiveDays = FileBridge.Core.Rules.ScheduleWindow.FromMask(job.ActiveDaysMask).ToList(),
            StabilitySeconds = job.StabilitySeconds, MaxParallelFiles = job.MaxParallelFiles,
            MaxRetries = job.MaxRetries, RetryBaseSeconds = job.RetryBaseSeconds,
            DuplicatePolicyId = job.DuplicatePolicyId, EncryptionProfileId = job.EncryptionProfileId,
            CompressionOperationId = job.CompressionOperationId, RenamePattern = job.RenamePattern,
            UseTempNameOnUpload = job.UseTempNameOnUpload, VerifyAfterUpload = job.VerifyAfterUpload,
            VirusScanEnabled = job.VirusScanEnabled, GenerateChecksumManifest = job.GenerateChecksumManifest,
            ValidateChecksumManifest = job.ValidateChecksumManifest, UseFileWatcher = job.UseFileWatcher,
            Version = job.Version, RowVersion = job.RowVersion,
            FolderMaps = job.FolderMaps.Select(m => new FolderMapModel { Id = m.Id, SourcePath = m.SourcePath, DestinationPath = m.DestinationPath, Recursive = m.Recursive, PreserveSubfolders = m.PreserveSubfolders }).ToList(),
            Filters = job.Filters.Select(f => new FilterModel { Id = f.Id, Pattern = f.Pattern, IsRegex = f.IsRegex, IsExclude = f.IsExclude, MinSizeBytes = f.MinSizeBytes, MaxSizeBytes = f.MaxSizeBytes, MinAgeSeconds = f.MinAgeSeconds, AllowedFileTypes = f.AllowedFileTypes }).ToList(),
            Semaphore = job.SemaphoreRule is null ? new() : new SemaphoreModel { SemaphoreModeId = job.SemaphoreRule.SemaphoreModeId, TriggerPattern = job.SemaphoreRule.TriggerPattern, DeleteTriggerAfter = job.SemaphoreRule.DeleteTriggerAfter, TransferTrigger = job.SemaphoreRule.TransferTrigger },
            PostAction = job.PostAction is null ? new() : new PostActionModel { PostActionTypeId = job.PostAction.PostActionTypeId, ArchivePath = job.PostAction.ArchivePath, RenamePattern = job.PostAction.RenamePattern },
            Notifications = job.NotificationRules.Select(n => new NotificationModel { Id = n.Id, NotificationEventId = n.NotificationEventId, NotificationChannelId = n.NotificationChannelId, Target = n.Target, IsEnabled = n.IsEnabled }).ToList(),
            SlaRules = job.SlaRules.Select(s => new SlaModel { Id = s.Id, ExpectedByLocalTime = s.ExpectedByLocalTime, ActiveDays = FileBridge.Core.Rules.ScheduleWindow.FromMask(s.DaysMask).ToList(), FilePattern = s.FilePattern, MinFileCount = s.MinFileCount, IsEnabled = s.IsEnabled }).ToList()
        };
    }

    public IReadOnlyList<string> Validate(JobEditModel m)
    {
        var errors = new List<string>();
        if (m.SourceEndpointId == m.DestinationEndpointId) errors.Add("Source and destination endpoints must be different.");
        if (m.FolderMaps.Count == 0) errors.Add("At least one folder map is required.");
        if (m.ScheduleTypeId == ScheduleType.Cron && string.IsNullOrWhiteSpace(m.CronExpression)) errors.Add("A cron expression is required for a cron schedule.");
        if (m.ScheduleTypeId == ScheduleType.Interval && (m.IntervalSeconds is null or < 15)) errors.Add("Interval must be at least 15 seconds.");
        if (!FileBridge.Core.Rules.ScheduleWindow.FindZone(m.TimeZoneId).Id.Equals(m.TimeZoneId, StringComparison.OrdinalIgnoreCase) && m.TimeZoneId != "Central Standard Time")
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(m.TimeZoneId); } catch { errors.Add($"'{m.TimeZoneId}' is not a recognized time zone."); }
        }
        foreach (var f in m.Filters)
            if (!FileBridge.Core.Rules.FileFilterEvaluator.TryValidate(new FileFilter { Pattern = f.Pattern, IsRegex = f.IsRegex }, out var err))
                errors.Add(err!);
        return errors;
    }

    /// <summary>If approval is required, stages the change in tblChangeRequest instead of applying it.</summary>
    public async Task<(bool Applied, string Message)> SubmitAsync(JobEditModel m)
    {
        var errors = Validate(m);
        if (errors.Count > 0) return (false, string.Join(" ", errors));

        if (await GlobalSettings.GetBoolAsync(db, SettingKeys.RequireApproval, default))
        {
            db.ChangeRequests.Add(new ChangeRequest
            {
                EntityName = "Job",
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
        return (true, "Job saved.");
    }

    public async Task ApplyAsync(JobEditModel m)
    {
        var job = m.Id == 0 ? new Job() : await db.Jobs.AsSplitQuery()
            .Include(j => j.FolderMaps).Include(j => j.Filters).Include(j => j.SemaphoreRule)
            .Include(j => j.PostAction).Include(j => j.NotificationRules).Include(j => j.SlaRules)
            .FirstAsync(j => j.Id == m.Id);

        if (m.Id != 0 && m.RowVersion is not null) db.Entry(job).Property(x => x.RowVersion).OriginalValue = m.RowVersion;

        job.Name = m.Name; job.Description = m.Description; job.TransferDirectionId = m.TransferDirectionId;
        job.SourceEndpointId = m.SourceEndpointId; job.DestinationEndpointId = m.DestinationEndpointId;
        job.IsEnabled = m.IsEnabled; job.IsPaused = m.IsPaused;
        job.ScheduleTypeId = m.ScheduleTypeId; job.CronExpression = m.CronExpression; job.IntervalSeconds = m.IntervalSeconds;
        job.TimeZoneId = m.TimeZoneId; job.ActiveFromTime = m.ActiveFromTime; job.ActiveToTime = m.ActiveToTime;
        job.ActiveDaysMask = FileBridge.Core.Rules.ScheduleWindow.ToMask(m.ActiveDays);
        job.StabilitySeconds = m.StabilitySeconds; job.MaxParallelFiles = m.MaxParallelFiles;
        job.MaxRetries = m.MaxRetries; job.RetryBaseSeconds = m.RetryBaseSeconds;
        job.DuplicatePolicyId = m.DuplicatePolicyId; job.EncryptionProfileId = m.EncryptionProfileId;
        job.CompressionOperationId = m.CompressionOperationId; job.RenamePattern = m.RenamePattern;
        job.UseTempNameOnUpload = m.UseTempNameOnUpload; job.VerifyAfterUpload = m.VerifyAfterUpload;
        job.VirusScanEnabled = m.VirusScanEnabled; job.GenerateChecksumManifest = m.GenerateChecksumManifest;
        job.ValidateChecksumManifest = m.ValidateChecksumManifest; job.UseFileWatcher = m.UseFileWatcher;
        job.Version++;

        Sync(job.FolderMaps, m.FolderMaps, (e, s) => { e.SourcePath = s.SourcePath; e.DestinationPath = s.DestinationPath; e.Recursive = s.Recursive; e.PreserveSubfolders = s.PreserveSubfolders; }, s => new JobFolderMap());
        Sync(job.Filters, m.Filters, (e, s) => { e.Pattern = s.Pattern; e.IsRegex = s.IsRegex; e.IsExclude = s.IsExclude; e.MinSizeBytes = s.MinSizeBytes; e.MaxSizeBytes = s.MaxSizeBytes; e.MinAgeSeconds = s.MinAgeSeconds; e.AllowedFileTypes = s.AllowedFileTypes; }, s => new FileFilter());
        Sync(job.NotificationRules, m.Notifications, (e, s) => { e.NotificationEventId = s.NotificationEventId; e.NotificationChannelId = s.NotificationChannelId; e.Target = s.Target; e.IsEnabled = s.IsEnabled; }, s => new NotificationRule());
        Sync(job.SlaRules, m.SlaRules, (e, s) => { e.ExpectedByLocalTime = s.ExpectedByLocalTime; e.DaysMask = FileBridge.Core.Rules.ScheduleWindow.ToMask(s.ActiveDays); e.FilePattern = s.FilePattern; e.MinFileCount = s.MinFileCount; e.IsEnabled = s.IsEnabled; }, s => new SlaRule());

        if (m.Semaphore.SemaphoreModeId == SemaphoreMode.None) { if (job.SemaphoreRule is not null) db.Remove(job.SemaphoreRule); job.SemaphoreRule = null; }
        else
        {
            job.SemaphoreRule ??= new SemaphoreRule();
            job.SemaphoreRule.SemaphoreModeId = m.Semaphore.SemaphoreModeId;
            job.SemaphoreRule.TriggerPattern = m.Semaphore.TriggerPattern;
            job.SemaphoreRule.DeleteTriggerAfter = m.Semaphore.DeleteTriggerAfter;
            job.SemaphoreRule.TransferTrigger = m.Semaphore.TransferTrigger;
        }

        if (m.PostAction.PostActionTypeId == PostActionType.None) { if (job.PostAction is not null) db.Remove(job.PostAction); job.PostAction = null; }
        else
        {
            job.PostAction ??= new PostAction();
            job.PostAction.PostActionTypeId = m.PostAction.PostActionTypeId;
            job.PostAction.ArchivePath = m.PostAction.ArchivePath;
            job.PostAction.RenamePattern = m.PostAction.RenamePattern;
        }

        if (job.Id == 0) db.Jobs.Add(job);
        await db.SaveChangesAsync();

        db.JobVersions.Add(new JobVersion { JobId = job.Id, Version = job.Version, SnapshotJson = JsonSerializer.Serialize(m), CreatedBy = user.Name, CreatedUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>Keyed sync of a job's child collections: updates matches by Id, adds new rows (Id == 0), removes the rest.</summary>
    private static void Sync<TEntity, TModel>(List<TEntity> existing, List<TModel> incoming, Action<TEntity, TModel> apply, Func<TModel, TEntity> create)
        where TEntity : class
    {
        var idProp = typeof(TEntity).GetProperty("Id")!;
        var modelIdProp = typeof(TModel)!.GetProperty("Id");
        var existingById = existing.ToDictionary(e => (int)idProp.GetValue(e)!);
        var keepIds = new HashSet<int>();

        foreach (var s in incoming)
        {
            var id = modelIdProp is null ? 0 : (int)(modelIdProp.GetValue(s) ?? 0);
            if (id != 0 && existingById.TryGetValue(id, out var entity)) { apply(entity, s); keepIds.Add(id); }
            else { var e = create(s); apply(e, s); existing.Add(e); }
        }
        existing.RemoveAll(e => (int)idProp.GetValue(e)! != 0 && !keepIds.Contains((int)idProp.GetValue(e)!));
    }

    public async Task ApproveAsync(long changeRequestId)
    {
        var cr = await db.ChangeRequests.FindAsync(changeRequestId) ?? throw new InvalidOperationException("Change request not found.");
        if (cr.EntityName != "Job") throw new NotSupportedException($"Approval for entity '{cr.EntityName}' is not implemented.");
        var model = JsonSerializer.Deserialize<JobEditModel>(cr.PayloadJson)!;
        await ApplyAsync(model);
        cr.ApprovalStatusId = ApprovalStatus.Approved;
        cr.ReviewedBy = user.Name;
        cr.ReviewedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RejectAsync(long changeRequestId, string? comment)
    {
        var cr = await db.ChangeRequests.FindAsync(changeRequestId) ?? throw new InvalidOperationException("Change request not found.");
        cr.ApprovalStatusId = ApprovalStatus.Rejected;
        cr.ReviewedBy = user.Name;
        cr.ReviewedUtc = DateTime.UtcNow;
        cr.ReviewComment = comment;
        await db.SaveChangesAsync();
    }

    public async Task RollbackAsync(int jobId, int version)
    {
        var snapshot = await db.JobVersions.AsNoTracking().FirstOrDefaultAsync(v => v.JobId == jobId && v.Version == version)
            ?? throw new InvalidOperationException($"Version {version} was not found for job {jobId}.");
        var model = JsonSerializer.Deserialize<JobEditModel>(snapshot.SnapshotJson)!;
        model.RowVersion = await db.Jobs.Where(j => j.Id == jobId).Select(j => j.RowVersion).FirstAsync();
        await ApplyAsync(model);
    }

    public async Task<int> CloneAsync(int jobId)
    {
        var model = await GetForEditAsync(jobId) ?? throw new InvalidOperationException("Job not found.");
        model.Id = 0; model.Version = 0; model.RowVersion = null; model.IsEnabled = false;
        model.Name = model.Name + " (copy)";
        foreach (var f in model.FolderMaps) f.Id = 0;
        foreach (var f in model.Filters) f.Id = 0;
        foreach (var n in model.Notifications) n.Id = 0;
        foreach (var s in model.SlaRules) s.Id = 0;
        await ApplyAsync(model);
        return await db.Jobs.Where(j => j.Name == model.Name).Select(j => j.Id).FirstAsync();
    }

    public async Task<bool> DeleteAsync(int jobId)
    {
        var hasHistory = await db.TransferHistories.AnyAsync(h => h.JobId == jobId);
        var job = await db.Jobs.FindAsync(jobId) ?? throw new InvalidOperationException("Job not found.");
        if (hasHistory) { job.IsEnabled = false; job.IsPaused = true; await db.SaveChangesAsync(); return false; }
        db.Jobs.Remove(job);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<string> ExportAsync(int jobId)
    {
        var model = await GetForEditAsync(jobId) ?? throw new InvalidOperationException("Job not found.");
        var sourceName = await db.Endpoints.Where(e => e.Id == model.SourceEndpointId).Select(e => e.Name).FirstAsync();
        var destName = await db.Endpoints.Where(e => e.Id == model.DestinationEndpointId).Select(e => e.Name).FirstAsync();
        var envelope = new { SourceEndpointName = sourceName, DestinationEndpointName = destName, Job = model };
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<int> ImportAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sourceName = root.GetProperty("SourceEndpointName").GetString()!;
        var destName = root.GetProperty("DestinationEndpointName").GetString()!;
        var model = JsonSerializer.Deserialize<JobEditModel>(root.GetProperty("Job").GetRawText())!;

        model.SourceEndpointId = await db.Endpoints.Where(e => e.Name == sourceName).Select(e => e.Id).FirstOrDefaultAsync();
        model.DestinationEndpointId = await db.Endpoints.Where(e => e.Name == destName).Select(e => e.Id).FirstOrDefaultAsync();
        if (model.SourceEndpointId == 0 || model.DestinationEndpointId == 0)
            throw new InvalidOperationException($"Endpoints '{sourceName}' / '{destName}' must exist in this environment before import.");

        model.Id = 0; model.Version = 0; model.RowVersion = null;
        foreach (var f in model.FolderMaps) f.Id = 0;
        foreach (var f in model.Filters) f.Id = 0;
        foreach (var n in model.Notifications) n.Id = 0;
        foreach (var s in model.SlaRules) s.Id = 0;
        await ApplyAsync(model);
        return await db.Jobs.Where(j => j.Name == model.Name).Select(j => j.Id).FirstAsync();
    }
}
