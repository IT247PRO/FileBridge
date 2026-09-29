using FileBridge.Admin.Models;
using FileBridge.Admin.Services;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

/// <summary>
/// Launches an external executable on a schedule and tracks it by PID (see ProcessJob/ProcessLaunchService).
/// Everything mutating requires Administer: running an arbitrary executable as the Worker service's own
/// identity is a materially higher-privilege action than editing transfer config.
/// </summary>
[Authorize(Policy = Security.Policies.Operate)]
public sealed class ProcessJobsController(FileBridgeDbContext db, ProcessJobService jobs, RequestService requests) : Controller
{
    public async Task<IActionResult> Index()
    {
        var list = await jobs.ListAsync();
        var jobIds = list.Select(j => j.Id).ToList();

        var latestIds = await db.ProcessRunHistories.AsNoTracking()
            .Where(h => jobIds.Contains(h.ProcessJobId) && h.StatusId != ProcessRunStatus.Starting && h.StatusId != ProcessRunStatus.Running)
            .GroupBy(h => h.ProcessJobId)
            .Select(g => g.OrderByDescending(x => x.StartedUtc).Select(x => x.Id).First())
            .ToListAsync();
        var lastStatusByJob = await db.ProcessRunHistories.AsNoTracking()
            .Where(h => latestIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.ProcessJobId, h => h.StatusId);
        var runningJobIds = await db.ProcessRunHistories.AsNoTracking()
            .Where(h => jobIds.Contains(h.ProcessJobId) && h.StatusId == ProcessRunStatus.Running)
            .Select(h => h.ProcessJobId).ToListAsync();

        ViewBag.Running = runningJobIds.ToHashSet();
        ViewBag.JobHealth = list.ToDictionary(j => j.Id, j => lastStatusByJob.TryGetValue(j.Id, out var s)
            ? s switch
            {
                ProcessRunStatus.Succeeded => ("Healthy", "ok"),
                ProcessRunStatus.Failed => ("Failing", "fail"),
                ProcessRunStatus.TimedOut => ("Timed out", "fail"),
                ProcessRunStatus.Killed => ("Killed last run", "warn"),
                ProcessRunStatus.Lost => ("Lost (Worker restarted)", "warn"),
                _ => ("Last run " + s.ToString().ToLowerInvariant(), "muted")
            }
            : ("Not yet run", "muted"));

        return View(list);
    }

    [Authorize(Policy = Security.Policies.Administer)]
    public IActionResult Create() => View("Edit", new ProcessJobEditModel());

    [Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await jobs.GetForEditAsync(id);
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Save(ProcessJobEditModel model)
    {
        var errors = jobs.Validate(model);
        foreach (var e in errors) ModelState.AddModelError("", e);
        if (!ModelState.IsValid) return View("Edit", model);

        var (applied, message) = await jobs.SubmitAsync(model);
        TempData["Message"] = message;
        TempData["MessageType"] = applied ? "success" : "info";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> TogglePause(int id)
    {
        var job = await db.ProcessJobs.FindAsync(id);
        if (job is null) return NotFound();
        job.IsPaused = !job.IsPaused;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> RunNow(int id)
    {
        var reqId = await requests.EnqueueAsync(RequestType.RunProcessNow, processJobId: id);
        return Json(new { requestId = reqId });
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Kill(int id)
    {
        // Looked up here (rather than left null) so the request only has to wait for the node that actually
        // owns the process; if nothing is running, ProcessLaunchService.KillAsync rejects it with a clear
        // error that flows back through the same request/poll UI as every other action.
        var runningNode = await db.ProcessRunHistories.AsNoTracking()
            .Where(h => h.ProcessJobId == id && h.StatusId == ProcessRunStatus.Running)
            .OrderByDescending(h => h.StartedUtc).Select(h => h.NodeName).FirstOrDefaultAsync();
        var reqId = await requests.EnqueueAsync(RequestType.KillProcess, processJobId: id, targetNode: runningNode);
        return Json(new { requestId = reqId });
    }

    public async Task<IActionResult> History(int id)
    {
        ViewBag.ProcessJobId = id;
        ViewBag.ProcessJobName = await db.ProcessJobs.Where(j => j.Id == id).Select(j => j.Name).FirstOrDefaultAsync();
        return View(await db.ProcessRunHistories.AsNoTracking().Where(h => h.ProcessJobId == id)
            .OrderByDescending(h => h.StartedUtc).Take(200).ToListAsync());
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await jobs.DeleteAsync(id);
        TempData["Message"] = deleted ? "Batch process job deleted." : "Batch process job has run history, so it was disabled instead of deleted.";
        TempData["MessageType"] = deleted ? "success" : "warning";
        return RedirectToAction(nameof(Index));
    }
}
