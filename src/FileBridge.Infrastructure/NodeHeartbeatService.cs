using System.Reflection;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FileBridge.Infrastructure;

/// <summary>Each node (Admin or Worker) upserts tblNodeHeartbeat every 30s so the dashboard shows cluster health.</summary>
public sealed class NodeHeartbeatService(IServiceScopeFactory scopes, string role, ILogger<NodeHeartbeatService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var node = Environment.MachineName;
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FileBridgeDbContext>();
                await db.Database.ExecuteSqlInterpolatedAsync($@"
MERGE dbo.tblNodeHeartbeat AS t
USING (SELECT {node} AS NodeName, {role} AS NodeRole) AS s
   ON t.NodeName = s.NodeName AND t.NodeRole = s.NodeRole
WHEN MATCHED THEN UPDATE SET LastSeenUtc = SYSUTCDATETIME(), AppVersion = {version},
     StartedUtc = CASE WHEN {first} = 1 THEN SYSUTCDATETIME() ELSE t.StartedUtc END
WHEN NOT MATCHED THEN INSERT (NodeName, NodeRole, LastSeenUtc, StartedUtc, AppVersion)
     VALUES (s.NodeName, s.NodeRole, SYSUTCDATETIME(), SYSUTCDATETIME(), {version});", ct);
                first = false;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Heartbeat write failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { }
        }
    }
}
