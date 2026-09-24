using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileBridge.Infrastructure.Engine;

/// <summary>Runs every minute; fires once per rule per day when a job's expected-by time passes without enough files.</summary>
public sealed class SlaEvaluator(FileBridgeDbContext db, INotifier notifier, ILogger<SlaEvaluator> log)
{
    public async Task EvaluateAsync(CancellationToken ct)
    {
        var rules = await db.SlaRules.AsNoTracking().Include(r => r.Job)
            .Where(r => r.IsEnabled && r.Job!.IsEnabled)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            var tz = ScheduleWindow.FindZone(rule.Job!.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var today = DateOnly.FromDateTime(localNow.DateTime);

            if ((rule.DaysMask & (1 << (int)localNow.DayOfWeek)) == 0) continue;
            if (TimeOnly.FromDateTime(localNow.DateTime) < rule.ExpectedByLocalTime) continue;
            if (rule.LastBreachDate == today) continue;

            var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, tz);
            var count = await db.TransferHistories.CountAsync(h =>
                h.JobId == rule.JobId && h.TransferStatusId == TransferStatus.Succeeded && h.StartedUtc >= dayStartUtc &&
                (rule.FilePattern == null || EF.Functions.Like(h.FileName, rule.FilePattern)), ct);

            if (count >= rule.MinFileCount) continue;

            log.LogWarning("SLA breach: job {JobId} expected {Min} file(s) by {ExpectedBy} local, saw {Count}",
                rule.JobId, rule.MinFileCount, rule.ExpectedByLocalTime, count);
            await notifier.NotifyAsync(NotificationEvent.SlaBreach, rule.JobId,
                $"FileBridge SLA breach: {rule.Job.Name}",
                $"Expected at least {rule.MinFileCount} file(s) by {rule.ExpectedByLocalTime} ({rule.Job.TimeZoneId}); saw {count}.", ct);

            await db.SlaRules.Where(r => r.Id == rule.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.LastBreachDate, today), ct);
        }
    }
}
