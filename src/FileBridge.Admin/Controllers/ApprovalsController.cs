using System.Text.Json;
using FileBridge.Admin.Services;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Approve)]
public sealed class ApprovalsController(FileBridgeDbContext db, JobService jobs, ProcessJobService processJobs) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ChangeRequests.AsNoTracking().Where(c => c.ApprovalStatusId == ApprovalStatus.Pending)
            .OrderBy(c => c.RequestedUtc).ToListAsync());

    public async Task<IActionResult> Details(long id)
    {
        var cr = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (cr is null) return NotFound();

        var pretty = new JsonSerializerOptions { WriteIndented = true };
        if (cr.EntityName == "Job" && cr.EntityKey is not null && int.TryParse(cr.EntityKey, out var jobId))
        {
            var current = await jobs.GetForEditAsync(jobId);
            if (current is not null) ViewBag.Current = JsonSerializer.Serialize(current, pretty);
        }
        else if (cr.EntityName == "ProcessJob" && cr.EntityKey is not null && int.TryParse(cr.EntityKey, out var processJobId))
        {
            var current = await processJobs.GetForEditAsync(processJobId);
            if (current is not null) ViewBag.Current = JsonSerializer.Serialize(current, pretty);
        }
        try
        {
            // Reformat for readability only; the stored payload itself is left untouched (this instance is AsNoTracking).
            cr.PayloadJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(cr.PayloadJson), pretty);
        }
        catch (JsonException) { /* leave as-is if it doesn't parse */ }

        return View(cr);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(long id)
    {
        var cr = await db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (cr is not null && cr.RequestedBy.Equals(User.Identity?.Name ?? "", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "You cannot approve your own change request.";
            TempData["MessageType"] = "warning";
            return RedirectToAction(nameof(Index));
        }
        if (cr?.EntityName == "ProcessJob")
            await processJobs.ApproveAsync(id);
        else
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
