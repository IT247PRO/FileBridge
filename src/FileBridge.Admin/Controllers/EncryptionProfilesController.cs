using FileBridge.Admin.Models;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Infrastructure.Crypto;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Admin.Controllers;

[Authorize(Policy = Security.Policies.Administer)]
public sealed class EncryptionProfilesController(FileBridgeDbContext db, ISecretProtector secrets) : Controller
{
    public async Task<IActionResult> Index() => View(await db.EncryptionProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync());

    public IActionResult Create() => View("Edit", new EncryptionProfileEditModel());

    public async Task<IActionResult> Edit(int id)
    {
        var p = await db.EncryptionProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        return View(new EncryptionProfileEditModel
        {
            Id = p.Id, Name = p.Name, EncryptionOperationId = p.EncryptionOperationId, OutputExtension = p.OutputExtension,
            StripExtensionOnDecrypt = p.StripExtensionOnDecrypt, ArmorOutput = p.ArmorOutput,
            HasStoredKeys = p.ProtectedPublicKey is not null || p.ProtectedPrivateKey is not null || p.ProtectedAesKey is not null,
            RowVersion = p.RowVersion
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(EncryptionProfileEditModel m)
    {
        if (!ModelState.IsValid) return View("Edit", m);

        var p = m.Id == 0 ? new EncryptionProfile() : await db.EncryptionProfiles.FirstAsync(x => x.Id == m.Id);
        if (m.Id != 0 && m.RowVersion is not null) db.Entry(p).Property(x => x.RowVersion).OriginalValue = m.RowVersion;

        p.Name = m.Name; p.EncryptionOperationId = m.EncryptionOperationId; p.OutputExtension = m.OutputExtension;
        p.StripExtensionOnDecrypt = m.StripExtensionOnDecrypt; p.ArmorOutput = m.ArmorOutput;
        if (!string.IsNullOrWhiteSpace(m.PublicKey)) p.ProtectedPublicKey = secrets.Protect(m.PublicKey);
        if (!string.IsNullOrWhiteSpace(m.PrivateKey)) p.ProtectedPrivateKey = secrets.Protect(m.PrivateKey);
        if (!string.IsNullOrWhiteSpace(m.Passphrase)) p.ProtectedPassphrase = secrets.Protect(m.Passphrase);

        string? generated = null;
        if (m.EncryptionOperationId is EncryptionOperation.AesEncrypt or EncryptionOperation.AesDecrypt && string.IsNullOrEmpty(p.ProtectedAesKey))
        {
            generated = CryptoService.GenerateAesKey();
            p.ProtectedAesKey = secrets.Protect(generated);
        }

        if (p.Id == 0) db.EncryptionProfiles.Add(p);
        await db.SaveChangesAsync();

        if (generated is not null) TempData["GeneratedAesKey"] = generated;
        TempData["Message"] = $"Encryption profile '{p.Name}' saved.";
        return RedirectToAction(nameof(Index));
    }
}
