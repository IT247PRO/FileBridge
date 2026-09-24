using FileBridge.Admin.Services;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Approve)]
public sealed class ApprovalsController(FileBridgeDbContext db, JobService jobs) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ChangeRequests.AsNoTracking().Where(c => c.ApprovalStatusId == ApprovalStatus.Pending)
            .OrderBy(c => c.RequestedUtc).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(long id)
    {
        var cr = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (cr is not null && cr.RequestedBy == User.Identity!.Name)
        {
            TempData["Message"] = "You cannot approve your own change request.";
            return RedirectToAction(nameof(Index));
        }
        await jobs.ApproveAsync(id);
        TempData["Message"] = "Change approved and applied.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(long id, string? comment)
    {
        await jobs.RejectAsync(id, comment);
        TempData["Message"] = "Change rejected.";
        return RedirectToAction(nameof(Index));
    }
}
