using System.Text.Json;
using FileBridge.Core;
using FileBridge.Core.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FileBridge.Infrastructure.Data;

/// <summary>Stamps Created/Modified and writes a before/after row to tblConfigAudit for every config change.</summary>
public sealed class ConfigAuditInterceptor(ICurrentUser user) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> result)
    {
        if (e.Context is not null) Capture(e.Context);
        return base.SavingChanges(e, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (e.Context is not null) Capture(e.Context);
        return base.SavingChangesAsync(e, result, ct);
    }

    private void Capture(DbContext ctx)
    {
        var now = DateTime.UtcNow;
        var entries = ctx.ChangeTracker.Entries()
            .Where(x => x.Entity is not IOperationalEntity and not LookupEntity and not DataProtectionKey)
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            if (entry.Entity is AuditableEntity a)
            {
                if (entry.State == EntityState.Added) { a.CreatedUtc = now; a.CreatedBy = user.Name; }
                else if (entry.State == EntityState.Modified) { a.ModifiedUtc = now; a.ModifiedBy = user.Name; }
            }

            ctx.Add(new ConfigAudit
            {
                EntityName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name,
                EntityKey = KeyOf(entry),
                AuditActionId = entry.State switch
                {
                    EntityState.Added => AuditAction.Added,
                    EntityState.Deleted => AuditAction.Deleted,
                    _ => AuditAction.Modified
                },
                BeforeJson = entry.State == EntityState.Added ? null : Serialize(entry.OriginalValues),
                AfterJson = entry.State == EntityState.Deleted ? null : Serialize(entry.CurrentValues),
                ChangedBy = user.Name,
                ChangedUtc = now,
                Host = Environment.MachineName
            });
        }
    }

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return "";
        var values = key.Properties.Select(p => entry.Property(p.Name));
        return entry.State == EntityState.Added && values.Any(v => v.IsTemporary)
            ? "(new)"
            : string.Join("|", values.Select(v => v.CurrentValue));
    }

    private static string Serialize(PropertyValues values)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in values.Properties)
        {
            if (p.Name == nameof(AuditableEntity.RowVersion)) continue;
            var v = values[p];
            dict[p.Name] = p.Name.StartsWith("Protected", StringComparison.Ordinal) || p.Name == nameof(NotificationRule.Target) && v is string s && s.StartsWith("CfDJ8")
                ? (v is null ? null : "***")
                : v;
        }
        return JsonSerializer.Serialize(dict);
    }
}
