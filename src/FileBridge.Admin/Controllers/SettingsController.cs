using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Administer)]
public sealed class SettingsController(FileBridgeDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Settings = await db.GlobalSettings.AsNoTracking().OrderBy(s => s.SettingKey).ToListAsync();
        ViewBag.RoleMappings = await db.RoleMappings.AsNoTracking().OrderBy(r => r.AdGroup).ToListAsync();
        ViewBag.GlobalBlackouts = await db.BlackoutWindows.AsNoTracking().Where(b => b.JobId == null).OrderBy(b => b.StartUtc).ToListAsync();
        ViewBag.GlobalNotifications = await db.NotificationRules.AsNoTracking().Where(n => n.JobId == null).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSetting(string key, string? value)
    {
        var setting = await db.GlobalSettings.FindAsync(key);
        if (setting is null) return NotFound();
        setting.SettingValue = value;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRoleMapping(string adGroup, AppRole role)
    {
        db.RoleMappings.Add(new RoleMapping { AdGroup = adGroup, AppRoleId = role });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRoleMapping(int id)
    {
        var m = await db.RoleMappings.FindAsync(id);
        if (m is not null) { db.RoleMappings.Remove(m); await db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBlackout(DateTime startUtc, DateTime endUtc, string? reason)
    {
        db.BlackoutWindows.Add(new BlackoutWindow { JobId = null, StartUtc = startUtc, EndUtc = endUtc, Reason = reason });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGlobalNotification(NotificationEvent evt, NotificationChannel channel, string target)
    {
        db.NotificationRules.Add(new NotificationRule { JobId = null, NotificationEventId = evt, NotificationChannelId = channel, Target = target, IsEnabled = true });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
