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
public sealed class EndpointsController(FileBridgeDbContext db, ISecretProtector secrets, RequestService requests) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.Endpoints.AsNoTracking().OrderBy(e => e.Name).ToListAsync());

    [Authorize(Policy = Security.Policies.Administer)]
    public IActionResult Create() => View("Edit", new EndpointEditModel());

    [Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Edit(int id)
    {
        var e = await db.Endpoints.AsNoTracking().Include(x => x.Credential).FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(new EndpointEditModel
        {
            Id = e.Id, Name = e.Name, EndpointTypeId = e.EndpointTypeId, Host = e.Host, Port = e.Port, BasePath = e.BasePath,
            HostKeyFingerprint = e.HostKeyFingerprint, BaseUrl = e.BaseUrl, ListRoute = e.ListRoute, DownloadRoute = e.DownloadRoute,
            UploadRoute = e.UploadRoute, DeleteRoute = e.DeleteRoute, RenameRoute = e.RenameRoute, TimeoutSeconds = e.TimeoutSeconds,
            IsEnabled = e.IsEnabled, Notes = e.Notes, CredentialName = e.Credential?.Name, Domain = e.Credential?.Domain,
            Username = e.Credential?.Username, HasStoredCredential = e.Credential is not null, RowVersion = e.RowVersion
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Security.Policies.Administer)]
    public async Task<IActionResult> Save(EndpointEditModel m)
    {
        if (!ModelState.IsValid) return View("Edit", m);

        var e = m.Id == 0 ? new FileBridge.Core.Entities.Endpoint() : await db.Endpoints.Include(x => x.Credential).FirstAsync(x => x.Id == m.Id);
        if (m.Id != 0 && m.RowVersion is not null) db.Entry(e).Property(x => x.RowVersion).OriginalValue = m.RowVersion;

        e.Name = m.Name; e.EndpointTypeId = m.EndpointTypeId; e.Host = m.Host; e.Port = m.Port; e.BasePath = m.BasePath;
        e.HostKeyFingerprint = m.HostKeyFingerprint; e.BaseUrl = m.BaseUrl; e.ListRoute = m.ListRoute; e.DownloadRoute = m.DownloadRoute;
        e.UploadRoute = m.UploadRoute; e.DeleteRoute = m.DeleteRoute; e.RenameRoute = m.RenameRoute; e.TimeoutSeconds = m.TimeoutSeconds;
        e.IsEnabled = m.IsEnabled; e.Notes = m.Notes;

        var wantsCredential = !string.IsNullOrWhiteSpace(m.Username) || !string.IsNullOrWhiteSpace(m.Password) || !string.IsNullOrWhiteSpace(m.PrivateKey);
        if (wantsCredential)
        {
            e.Credential ??= new Credential { Name = m.Name + " credential" };
            e.Credential.Name = m.Name + " credential";
            e.Credential.Domain = m.Domain;
            if (!string.IsNullOrWhiteSpace(m.Username)) e.Credential.Username = m.Username;
            if (!string.IsNullOrWhiteSpace(m.Password)) e.Credential.ProtectedPassword = secrets.Protect(m.Password);
            if (!string.IsNullOrWhiteSpace(m.PrivateKey)) e.Credential.ProtectedPrivateKey = secrets.Protect(m.PrivateKey);
            if (!string.IsNullOrWhiteSpace(m.Passphrase)) e.Credential.ProtectedPassphrase = secrets.Protect(m.Passphrase);
        }

        if (e.Id == 0) db.Endpoints.Add(e);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Endpoint '{e.Name}' saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> TestConnection(int id)
    {
        var reqId = await requests.EnqueueAsync(RequestType.TestConnection, endpointId: id);
        return Json(new { requestId = reqId });
    }

    [HttpPost]
    public async Task<IActionResult> Browse(int id, string? path)
    {
        var reqId = await requests.EnqueueAsync(RequestType.Browse, endpointId: id, path: path ?? "");
        return Json(new { requestId = reqId });
    }
}
