using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Services;

/// <summary>
/// The Admin tier never touches file systems: every action that needs to (test, browse, run, dry-run,
/// release/discard quarantine) is enqueued here as a tblRunRequest for a Worker node to pick up.
/// </summary>
public sealed class RequestService(FileBridgeDbContext db, ICurrentUser user)
{
    public async Task<long> EnqueueAsync(RequestType type, int? jobId = null, int? endpointId = null, int? quarantineId = null, string? path = null)
    {
        var req = new RunRequest
        {
            RequestTypeId = type, JobId = jobId, EndpointId = endpointId, QuarantineId = quarantineId, Path = path,
            RequestStatusId = RequestStatus.Queued, RequestedBy = user.Name, RequestedUtc = DateTime.UtcNow
        };
        db.RunRequests.Add(req);
        await db.SaveChangesAsync();
        return req.Id;
    }

    public async Task<RunRequest?> GetAsync(long id) => await db.RunRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
}
