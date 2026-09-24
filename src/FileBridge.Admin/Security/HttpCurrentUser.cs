using FileBridge.Core;
using Microsoft.AspNetCore.Http;

namespace FileBridge.Admin.Security;

/// <summary>The signed-in Windows identity (DOMAIN\user), stamped on audit rows and shown in the nav.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Name => accessor.HttpContext?.User?.Identity?.Name ?? "unknown";
}
