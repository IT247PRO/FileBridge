using FileBridge.Admin.Services;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Operate)]
public sealed class QuarantineController(FileBridgeDbContext db, RequestService requests) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.Quarantines.AsNoTracking().Include(q => q.Job)
            .Where(q => q.QuarantineStatusId == QuarantineStatus.Held)
            .OrderByDescending(q => q.CreatedUtc).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Release(int id)
    {
        var reqId = await requests.EnqueueAsync(RequestType.ReleaseQuarantine, quarantineId: id);
        return Json(new { requestId = reqId });
    }

    [HttpPost]
    public async Task<IActionResult> Discard(int id)
    {
        var reqId = await requests.EnqueueAsync(RequestType.DiscardQuarantine, quarantineId: id);
        return Json(new { requestId = reqId });
    }
}
