using System.Security.Claims;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace FileBridge.Admin.Security;

/// <summary>
/// Adds ClaimTypes.Role for every tblRoleMapping row matching the user's name or any of their Windows group
/// SIDs, plus Security:BootstrapAdmins from config so the very first admin can sign in before any mapping exists.
/// Cached for 2 minutes per principal name to avoid a DB round trip on every request.
/// </summary>
public sealed class RoleClaimsTransformation(IDbContextFactory<FileBridgeDbContext> dbFactory, IMemoryCache cache, IConfiguration config) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is not { IsAuthenticated: true, Name: { } name }) return principal;
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role)) return principal;

        var cacheKey = $"roles:{name}";
        var roles = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            // Group-SID-to-name resolution is a directory lookup left for the deployment team (see README "Role mapping");
            // until then, role mapping matches on username only.
            // A List<string> (not string[]) avoids .NET 8+ picking the span-based Contains overload,
            // which EF Core's expression interpreter can't evaluate (ReadOnlySpan<T> is a ref struct).
            var candidates = new List<string> { name };

            await using var db = await dbFactory.CreateDbContextAsync();
            var mapped = await db.RoleMappings.AsNoTracking()
                .Where(m => candidates.Contains(m.AdGroup))
                .Select(m => m.AppRoleId.ToString())
                .Distinct().ToListAsync();

            var bootstrap = config.GetSection("Security:BootstrapAdmins").Get<string[]>() ?? Array.Empty<string>();
            if (bootstrap.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase))) mapped.Add("Admin");
            return mapped;
        }) ?? new List<string>();

        var clone = (ClaimsIdentity)identity.Clone();
        foreach (var role in roles.Distinct()) clone.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(clone);
    }
}
