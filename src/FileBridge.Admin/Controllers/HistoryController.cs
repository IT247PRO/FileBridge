using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.View)]
public sealed class HistoryController(FileBridgeDbContext db) : Controller
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(int? jobId, TransferStatus? status, int page = 1)
    {
        var q = db.TransferHistories.AsNoTracking().Include(h => h.Job).AsQueryable();
        if (jobId is not null) q = q.Where(h => h.JobId == jobId);
        if (status is not null) q = q.Where(h => h.TransferStatusId == status);

        ViewBag.JobId = jobId; ViewBag.Status = status; ViewBag.Page = page;
        ViewBag.Total = await q.CountAsync();
        ViewBag.Jobs = await db.Jobs.AsNoTracking().OrderBy(j => j.Name).ToListAsync();
        return View(await q.OrderByDescending(h => h.StartedUtc).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync());
    }

    public async Task<IActionResult> Details(long id)
    {
        var h = await db.TransferHistories.AsNoTracking().Include(x => x.Job).FirstOrDefaultAsync(x => x.Id == id);
        return h is null ? NotFound() : View(h);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Security.Policies.Operate)]
    public async Task<IActionResult> Reprocess(long id)
    {
        var h = await db.TransferHistories.FindAsync(id);
        if (h is null) return NotFound();
        // Fingerprint includes mtime ticks, which this row doesn't retain, so clear by (JobId, SourcePath) instead.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.tblFileLease SET CompletedUtc = NULL, LeaseExpiresUtc = SYSUTCDATETIME()
WHERE JobId = {h.JobId} AND SourcePath = {h.SourcePath}");
        TempData["Message"] = "File will be re-attempted on the next run (lease cleared).";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> ExportCsv(int? jobId, TransferStatus? status)
    {
        var q = db.TransferHistories.AsNoTracking().Include(h => h.Job).AsQueryable();
        if (jobId is not null) q = q.Where(h => h.JobId == jobId);
        if (status is not null) q = q.Where(h => h.TransferStatusId == status);
        var rows = await q.OrderByDescending(h => h.StartedUtc).Take(10000).ToListAsync();

        var sb = new System.Text.StringBuilder("Job,File,Status,SizeBytes,StartedUtc,DurationMs,Error\n");
        foreach (var r in rows)
            sb.AppendLine($"\"{r.Job?.Name}\",\"{r.FileName}\",{r.TransferStatusId},{r.SizeBytes},{r.StartedUtc:O},{r.DurationMs},\"{r.ErrorMessage?.Replace("\"", "\"\"")}\"");
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "filebridge-history.csv");
    }
}
