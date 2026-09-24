using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Infrastructure.Engine;

/// <summary>
/// Exactly-once claim across Worker nodes, backed by the unique index on tblFileLease(JobId, Fingerprint).
/// A completed lease means "already handled" (the file is never picked up again).
/// A failed lease is retried after a cooldown, up to MaxLeaseAttempts; an expired in-progress lease (crashed node) is taken over.
/// </summary>
public static class Leases
{
    public static async Task<long?> TryAcquireAsync(FileBridgeDbContext db, int jobId, string fingerprint, string path,
        string node, TimeSpan duration, int maxAttempts, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var until = now + duration;
        var lease = new FileLease
        {
            JobId = jobId, Fingerprint = fingerprint, SourcePath = Truncate(path, 1000), LeasedBy = node,
            LeaseExpiresUtc = until, TransferStatusId = TransferStatus.InProgress, CreatedUtc = now
        };
        db.FileLeases.Add(lease);
        try
        {
            await db.SaveChangesAsync(ct);
            return lease.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            db.Entry(lease).State = EntityState.Detached;
            var taken = await db.FileLeases
                .Where(l => l.JobId == jobId && l.Fingerprint == fingerprint && l.CompletedUtc == null
                            && l.LeaseExpiresUtc < now && l.AttemptCount < maxAttempts)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.LeasedBy, node)
                    .SetProperty(l => l.LeaseExpiresUtc, until)
                    .SetProperty(l => l.TransferStatusId, TransferStatus.InProgress)
                    .SetProperty(l => l.AttemptCount, l => l.AttemptCount + 1), ct);
            if (taken == 0) return null;
            return await db.FileLeases.Where(l => l.JobId == jobId && l.Fingerprint == fingerprint)
                .Select(l => (long?)l.Id).FirstAsync(ct);
        }
    }

    public static Task CompleteAsync(FileBridgeDbContext db, long leaseId, TransferStatus status, CancellationToken ct) =>
        db.FileLeases.Where(l => l.Id == leaseId).ExecuteUpdateAsync(s => s
            .SetProperty(l => l.CompletedUtc, DateTime.UtcNow)
            .SetProperty(l => l.TransferStatusId, status), ct);

    public static Task FailAsync(FileBridgeDbContext db, long leaseId, TimeSpan cooldown, CancellationToken ct) =>
        db.FileLeases.Where(l => l.Id == leaseId).ExecuteUpdateAsync(s => s
            .SetProperty(l => l.TransferStatusId, TransferStatus.Failed)
            .SetProperty(l => l.LeaseExpiresUtc, DateTime.UtcNow + cooldown), ct);

    /// <summary>Forget a file so it is picked up again (Reprocess in the UI).</summary>
    public static Task<int> ReleaseAsync(FileBridgeDbContext db, int jobId, string sourcePath, CancellationToken ct) =>
        db.FileLeases.Where(l => l.JobId == jobId && l.SourcePath == sourcePath).ExecuteDeleteAsync(ct);

    internal static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
