using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Services;

/// <summary>Whether a Worker node is currently reporting a heartbeat (see NodeHeartbeatService). Backs the
/// site-wide alert bar in _Layout.cshtml, since a dead Worker means no scheduled transfer or batch process
/// job runs anywhere in the cluster.</summary>
public sealed record WorkerHealthStatus(bool HasAnyWorker, bool Healthy, DateTime? LastSeenUtc);

public sealed class ClusterHealthService(FileBridgeDbContext db)
{
    // Matches the "Stale" threshold already used on the dashboard's Cluster nodes card.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    public async Task<WorkerHealthStatus> GetWorkerStatusAsync(CancellationToken ct = default)
    {
        var lastSeenUtc = await db.NodeHeartbeats.AsNoTracking()
            .Where(n => n.NodeRole == "Worker")
            .OrderByDescending(n => n.LastSeenUtc)
            .Select(n => (DateTime?)n.LastSeenUtc)
            .FirstOrDefaultAsync(ct);

        if (lastSeenUtc is null) return new WorkerHealthStatus(HasAnyWorker: false, Healthy: false, LastSeenUtc: null);

        var healthy = DateTime.UtcNow - lastSeenUtc.Value <= StaleAfter;
        return new WorkerHealthStatus(HasAnyWorker: true, Healthy: healthy, LastSeenUtc: lastSeenUtc);
    }
}
