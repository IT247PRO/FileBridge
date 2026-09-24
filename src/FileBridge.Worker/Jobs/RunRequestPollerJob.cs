using System.Text.Json;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Connectors;
using FileBridge.Infrastructure.Data;
using FileBridge.Infrastructure.Engine;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace FileBridge.Worker.Jobs;

/// <summary>
/// The Admin UI never touches endpoints directly; it enqueues a tblRunRequest and polls the result via
/// GET /api/v1/requests/{id}. This job claims queued requests (any Worker node, first to update wins because
/// of the WHERE RequestStatusId = Queued guard) and executes them.
/// </summary>
public sealed class RunRequestPollerJob(FileBridgeDbContext db, TransferPipeline pipeline, IEndpointFactory endpoints, ISchedulerFactory schedulerFactory) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var node = Environment.MachineName;

        var claimed = await db.RunRequests
            .Where(r => r.RequestStatusId == RequestStatus.Queued)
            .OrderBy(r => r.Id)
            .Take(10)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RequestStatusId, RequestStatus.Running)
                .SetProperty(r => r.PickedBy, node)
                .SetProperty(r => r.StartedUtc, DateTime.UtcNow), ct);
        if (claimed == 0) return;

        var requests = await db.RunRequests
            .Where(r => r.RequestStatusId == RequestStatus.Running && r.PickedBy == node && r.CompletedUtc == null)
            .OrderBy(r => r.Id).Take(10).ToListAsync(ct);

        foreach (var req in requests)
        {
            object? result;
            var ok = true;
            try
            {
                result = req.RequestTypeId switch
                {
                    RequestType.RunNow => await RunNowAsync(req.JobId!.Value, req.RequestedBy, ct),
                    RequestType.DryRun => await pipeline.RunAsync(req.JobId!.Value, req.RequestedBy, dryRun: true, ct),
                    RequestType.TestConnection => await TestConnectionAsync(req.EndpointId!.Value, ct),
                    RequestType.Browse => await BrowseAsync(req.EndpointId!.Value, req.Path ?? "", ct),
                    RequestType.ReleaseQuarantine => await ReleaseAsync(req.QuarantineId!.Value, req.RequestedBy, ct),
                    RequestType.DiscardQuarantine => await DiscardAsync(req.QuarantineId!.Value, req.RequestedBy, ct),
                    _ => throw new NonRetryableException($"Unknown request type {req.RequestTypeId}.")
                };
            }
            catch (Exception ex)
            {
                ok = false;
                result = new { error = ex.Message };
            }

            req.RequestStatusId = ok ? RequestStatus.Completed : RequestStatus.Failed;
            req.CompletedUtc = DateTime.UtcNow;
            req.ResultJson = JsonSerializer.Serialize(result);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<object> RunNowAsync(int jobId, string requestedBy, CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var data = new JobDataMap { ["jobId"] = jobId.ToString(), ["triggeredBy"] = $"manual:{requestedBy}", ["manual"] = "true" };
        await scheduler.TriggerJob(new JobKey($"transfer-{jobId}", "transfer"), data, ct);
        return new { queued = true };
    }

    private async Task<ConnectionTestResult> TestConnectionAsync(int endpointId, CancellationToken ct)
    {
        var endpoint = await db.Endpoints.Include(e => e.Credential).FirstOrDefaultAsync(e => e.Id == endpointId, ct)
            ?? throw new NonRetryableException($"Endpoint {endpointId} was not found.");
        await using var conn = endpoints.Create(endpoint);
        return await conn.TestAsync(ct);
    }

    private async Task<object> BrowseAsync(int endpointId, string path, CancellationToken ct)
    {
        var endpoint = await db.Endpoints.Include(e => e.Credential).FirstOrDefaultAsync(e => e.Id == endpointId, ct)
            ?? throw new NonRetryableException($"Endpoint {endpointId} was not found.");
        await using var conn = endpoints.Create(endpoint);
        await conn.ConnectAsync(ct);
        var folders = await conn.ListFoldersAsync(path, ct);
        var files = (await conn.ListAsync(path, false, ct)).Take(200).ToList();
        return new { folders, files };
    }

    private async Task<object> ReleaseAsync(int quarantineId, string by, CancellationToken ct)
    {
        await pipeline.ReleaseQuarantineAsync(quarantineId, by, ct);
        return new { released = true };
    }

    private async Task<object> DiscardAsync(int quarantineId, string by, CancellationToken ct)
    {
        await pipeline.DiscardQuarantineAsync(quarantineId, by, ct);
        return new { discarded = true };
    }
}
