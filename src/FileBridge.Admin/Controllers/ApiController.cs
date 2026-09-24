using FileBridge.Admin.Services;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

/// <summary>
/// Thin JSON API behind the same cookie/Windows auth as the rest of the site. site.js polls
/// GET requests/{id} after enqueueing a TestConnection/Browse/DryRun/RunNow request.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = Security.Policies.View)]
[IgnoreAntiforgeryToken]
public sealed class ApiController(FileBridgeDbContext db, RequestService requests) : ControllerBase
{
    [HttpGet("requests/{id:long}")]
    public async Task<IActionResult> GetRequest(long id)
    {
        var r = await requests.GetAsync(id);
        if (r is null) return NotFound();
        return Ok(new { r.Id, Status = r.RequestStatusId.ToString(), r.ResultJson, r.CompletedUtc });
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs() =>
        Ok(await db.Jobs.AsNoTracking().Select(j => new { j.Id, j.Name, j.IsEnabled, j.IsPaused }).ToListAsync());

    [HttpPost("jobs/{id:int}/run"), Authorize(Policy = Security.Policies.Operate)]
    public async Task<IActionResult> RunJob(int id) =>
        Ok(new { requestId = await requests.EnqueueAsync(RequestType.RunNow, jobId: id) });

    [HttpGet("jobs/{id:int}/status")]
    public async Task<IActionResult> JobStatus(int id)
    {
        var last = await db.TransferHistories.AsNoTracking().Where(h => h.JobId == id)
            .OrderByDescending(h => h.StartedUtc).Select(h => new { h.StartedUtc, Status = h.TransferStatusId.ToString() }).FirstOrDefaultAsync();
        return Ok(last);
    }
}
