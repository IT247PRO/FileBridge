using FileBridge.Admin.Models;
using FileBridge.Admin.Services;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Operate)]
public sealed class JobsController(FileBridgeDbContext db, JobService jobs, RequestService requests) : Controller
{
    public async Task<IActionResult> Index()
    {
        var list = await jobs.ListAsync();
        var jobIds = list.Select(j => j.Id).ToList();

        // Health = outcome of each job's most recently completed run (in-progress rows don't count).
        var latestIds = await db.TransferHistories.AsNoTracking()
            .Where(h => jobIds.Contains(h.JobId) && h.TransferStatusId != TransferStatus.InProgress)
            .GroupBy(h => h.JobId)
            .Select(g => g.OrderByDescending(x => x.StartedUtc).Select(x => x.Id).First())
            .ToListAsync();
        var lastStatusByJob = await db.TransferHistories.AsNoTracking()
            .Where(h => latestIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.JobId, h => h.TransferStatusId);

        ViewBag.JobHealth = list.ToDictionary(j => j.Id, j => lastStatusByJob.TryGetValue(j.Id, out var s)
            ? s switch
            {
                TransferStatus.Succeeded => ("Healthy", "ok"),
                TransferStatus.Failed => ("Failing", "fail"),
                TransferStatus.Quarantined => ("Held for review", "quarantine"),
                _ => ("Last run " + s.ToString().ToLowerInvariant(), "muted")
            }
            : ("Not yet run", "muted"));

        return View(list);
    }

    [Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        return View("Edit", new JobEditModel());
    }

    [Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await jobs.GetForEditAsync(id);
        if (model is null) return NotFound();
        await PopulateLookupsAsync();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Save(JobEditModel model)
    {
        var errors = jobs.Validate(model);
        foreach (var e in errors) ModelState.AddModelError("", e);
        if (!ModelState.IsValid) { await PopulateLookupsAsync(); return View("Edit", model); }

        var (applied, message) = await jobs.SubmitAsync(model);
        TempData["Message"] = message;
        TempData["MessageType"] = applied ? "success" : "info";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> TogglePause(int id)
    {
        var job = await db.Jobs.FindAsync(id);
        if (job is null) return NotFound();
        job.IsPaused = !job.IsPaused;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RunNow(int id)
    {
        var reqId = await requests.EnqueueAsync(RequestType.RunNow, jobId: id);
        return Json(new { requestId = reqId });
    }

    [HttpPost]
    public async Task<IActionResult> DryRun(int id)
    {
        var reqId = await requests.EnqueueAsync(RequestType.DryRun, jobId: id);
        return Json(new { requestId = reqId });
    }

    public async Task<IActionResult> Versions(int id)
    {
        ViewBag.JobId = id;
        ViewBag.JobName = await db.Jobs.Where(j => j.Id == id).Select(j => j.Name).FirstOrDefaultAsync();
        return View(await db.JobVersions.AsNoTracking().Where(v => v.JobId == id).OrderByDescending(v => v.Version).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Rollback(int id, int version)
    {
        await jobs.RollbackAsync(id, version);
        TempData["Message"] = $"Rolled back to version {version}.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Clone(int id)
    {
        var newId = await jobs.CloneAsync(id);
        TempData["Message"] = "Job cloned (disabled by default). Review and enable it.";
        return RedirectToAction(nameof(Edit), new { id = newId });
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await jobs.DeleteAsync(id);
        TempData["Message"] = deleted ? "Job deleted." : "Job has run history, so it was disabled instead of deleted.";
        TempData["MessageType"] = deleted ? "success" : "warning";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Export(int id)
    {
        var json = await jobs.ExportAsync(id);
        var name = await db.Jobs.Where(j => j.Id == id).Select(j => j.Name).FirstAsync();
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"{name}.filebridge-job.json");
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Import(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var json = await reader.ReadToEndAsync();
        var id = await jobs.ImportAsync(json);
        TempData["Message"] = "Job imported.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task PopulateLookupsAsync()
    {
        ViewBag.Endpoints = await db.Endpoints.AsNoTracking().Where(e => e.IsEnabled).OrderBy(e => e.Name).ToListAsync();
        ViewBag.EncryptionProfiles = await db.EncryptionProfiles.AsNoTracking().OrderBy(e => e.Name).ToListAsync();
        ViewBag.TimeZones = TimeZoneInfo.GetSystemTimeZones();
    }
}
