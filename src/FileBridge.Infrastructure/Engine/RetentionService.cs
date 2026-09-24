using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileBridge.Infrastructure.Engine;

/// <summary>Nightly purge, driven by tblGlobalSetting retention-day values (see db/04_seed_config.sql).</summary>
public sealed class RetentionService(FileBridgeDbContext db, ILogger<RetentionService> log)
{
    private const int BatchSize = 5000;

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await PurgeAsync("tblTransferHistory", ct, async () =>
        {
            var cutoff = now.AddDays(-await GlobalSettings.GetIntAsync(db, SettingKeys.HistoryDays, 90, ct));
            return await db.TransferHistories.Where(h => h.StartedUtc < cutoff).OrderBy(h => h.Id).Take(BatchSize).ExecuteDeleteAsync(ct);
        });

        await PurgeAsync("tblFileLease", ct, async () =>
        {
            var cutoff = now.AddDays(-await GlobalSettings.GetIntAsync(db, SettingKeys.LeaseDays, 30, ct));
            return await db.FileLeases.Where(l => l.CreatedUtc < cutoff && l.CompletedUtc != null).OrderBy(l => l.Id).Take(BatchSize).ExecuteDeleteAsync(ct);
        });

        await PurgeAsync("tblRunRequest", ct, async () =>
        {
            var cutoff = now.AddDays(-await GlobalSettings.GetIntAsync(db, SettingKeys.RequestDays, 14, ct));
            return await db.RunRequests.Where(r => r.RequestedUtc < cutoff).OrderBy(r => r.Id).Take(BatchSize).ExecuteDeleteAsync(ct);
        });

        await PurgeAsync("tblConfigAudit", ct, async () =>
        {
            var cutoff = now.AddDays(-await GlobalSettings.GetIntAsync(db, SettingKeys.AuditDays, 365, ct));
            return await db.ConfigAudits.Where(a => a.ChangedUtc < cutoff).OrderBy(a => a.Id).Take(BatchSize).ExecuteDeleteAsync(ct);
        });

        await PurgeAsync("tblNodeHeartbeat (stale)", ct, async () =>
            await db.NodeHeartbeats.Where(h => h.LastSeenUtc < now.AddDays(-1)).ExecuteDeleteAsync(ct));

        var quarantineCutoff = now.AddDays(-await GlobalSettings.GetIntAsync(db, SettingKeys.QuarantineDays, 60, ct));
        var stale = await db.Quarantines
            .Where(q => q.QuarantineStatusId != QuarantineStatus.Held && q.ReviewedUtc != null && q.ReviewedUtc < quarantineCutoff)
            .Take(BatchSize).ToListAsync(ct);
        foreach (var q in stale) { try { if (File.Exists(q.QuarantinePath)) File.Delete(q.QuarantinePath); } catch { /* best effort */ } }
        if (stale.Count > 0)
        {
            db.Quarantines.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
            log.LogInformation("Retention: removed {Count} reviewed quarantine row(s) and files", stale.Count);
        }

        var logCutoff = now.AddDays(-await GlobalSettings.GetIntAsync(db, SettingKeys.LogDays, 30, ct));
        var deletedLogs = await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE TOP ({BatchSize}) FROM dbo.tblApplicationLog WHERE TimeStamp < {logCutoff}", ct);
        if (deletedLogs > 0) log.LogInformation("Retention: removed {Count} tblApplicationLog row(s)", deletedLogs);
    }

    private async Task PurgeAsync(string what, CancellationToken ct, Func<Task<int>> delete)
    {
        int total = 0, batch;
        do { batch = await delete(); total += batch; } while (batch == BatchSize && !ct.IsCancellationRequested);
        if (total > 0) log.LogInformation("Retention: removed {Count} row(s) from {Table}", total, what);
    }
}
