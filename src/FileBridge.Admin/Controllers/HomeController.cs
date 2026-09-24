using FileBridge.Admin.Models;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize]
public sealed class HomeController(FileBridgeDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var vm = new DashboardViewModel
        {
            SucceededToday = await db.TransferHistories.CountAsync(h => h.StartedUtc >= todayUtc && h.TransferStatusId == TransferStatus.Succeeded),
            FailedToday = await db.TransferHistories.CountAsync(h => h.StartedUtc >= todayUtc && h.TransferStatusId == TransferStatus.Failed),
            InProgress = await db.TransferHistories.CountAsync(h => h.TransferStatusId == TransferStatus.InProgress),
            BytesToday = await db.TransferHistories.Where(h => h.StartedUtc >= todayUtc && h.TransferStatusId == TransferStatus.Succeeded).SumAsync(h => (long?)h.SizeBytes) ?? 0,
            HeldQuarantine = await db.Quarantines.CountAsync(q => q.QuarantineStatusId == QuarantineStatus.Held),
            PendingApprovals = await db.ChangeRequests.CountAsync(c => c.ApprovalStatusId == ApprovalStatus.Pending),
            KillSwitchOn = await GlobalSettings.GetBoolAsync(db, SettingKeys.KillSwitch, default),
            Nodes = await db.NodeHeartbeats.AsNoTracking().OrderBy(n => n.NodeRole).ThenBy(n => n.NodeName)
                .Select(n => new ValueTuple<string, string, DateTime>(n.NodeName, n.NodeRole, n.LastSeenUtc)).ToListAsync()
        };

        var recentFailingRows = await db.TransferHistories.AsNoTracking()
            .Where(h => h.StartedUtc >= todayUtc && h.TransferStatusId == TransferStatus.Failed)
            .GroupBy(h => h.Job!.Name)
            .Select(g => new { JobName = g.Key, Failed = g.Count(), LastFailureUtc = g.Max(x => x.StartedUtc) })
            .OrderByDescending(x => x.Failed).Take(10).ToListAsync();
        vm.RecentFailingJobs = recentFailingRows.Select(r => (r.JobName, r.Failed, r.LastFailureUtc)).ToList();

        return View(vm);
    }

    [HttpPost, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> ToggleKillSwitch()
    {
        var on = await GlobalSettings.GetBoolAsync(db, SettingKeys.KillSwitch, default);
        var setting = await db.GlobalSettings.FindAsync(SettingKeys.KillSwitch) ?? throw new InvalidOperationException("Setting not seeded.");
        setting.SettingValue = (!on).ToString();
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
