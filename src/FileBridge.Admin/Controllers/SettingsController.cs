using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Administer)]
public sealed class SettingsController(FileBridgeDbContext db, ISecretProtector secrets) : Controller
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
        if (setting.IsSecret)
        {
            if (!string.IsNullOrWhiteSpace(value))
                setting.SettingValue = secrets.Protect(value);
        }
        else
        {
            setting.SettingValue = value;
        }
        await db.SaveChangesAsync();
        TempData["Message"] = $"'{key}' updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRoleMapping(string adGroup, AppRole role)
    {
        db.RoleMappings.Add(new RoleMapping { AdGroup = adGroup, AppRoleId = role });
        await db.SaveChangesAsync();
        TempData["Message"] = $"Role mapping added for '{adGroup}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRoleMapping(int id)
    {
        var m = await db.RoleMappings.FindAsync(id);
        if (m is not null) { db.RoleMappings.Remove(m); await db.SaveChangesAsync(); }
        TempData["Message"] = "Role mapping removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBlackout(DateTime startUtc, DateTime endUtc, string? reason)
    {
        db.BlackoutWindows.Add(new BlackoutWindow { JobId = null, StartUtc = startUtc, EndUtc = endUtc, Reason = reason });
        await db.SaveChangesAsync();
        TempData["Message"] = "Global blackout window added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGlobalNotification(NotificationEvent evt, NotificationChannel channel, string target)
    {
        db.NotificationRules.Add(new NotificationRule { JobId = null, NotificationEventId = evt, NotificationChannelId = channel, Target = target, IsEnabled = true });
        await db.SaveChangesAsync();
        TempData["Message"] = "Global notification rule added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBlackout(int id)
    {
        var b = await db.BlackoutWindows.FirstOrDefaultAsync(x => x.Id == id && x.JobId == null);
        if (b is not null) { db.BlackoutWindows.Remove(b); await db.SaveChangesAsync(); }
        TempData["Message"] = "Global blackout window removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveGlobalNotification(int id)
    {
        var n = await db.NotificationRules.FirstOrDefaultAsync(x => x.Id == id && x.JobId == null);
        if (n is not null) { db.NotificationRules.Remove(n); await db.SaveChangesAsync(); }
        TempData["Message"] = "Global notification rule removed.";
        return RedirectToAction(nameof(Index));
    }
}
