using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Administer)]
public sealed class AuditController(FileBridgeDbContext db) : Controller
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(string? entity, int page = 1)
    {
        var q = db.ConfigAudits.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entity)) q = q.Where(a => a.EntityName == entity);
        ViewBag.Entity = entity; ViewBag.Page = page;
        ViewBag.Total = await q.CountAsync();
        ViewBag.Entities = await db.ConfigAudits.AsNoTracking().Select(a => a.EntityName).Distinct().OrderBy(x => x).ToListAsync();
        return View(await q.OrderByDescending(a => a.ChangedUtc).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync());
    }
}
