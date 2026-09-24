using FileBridge.Core;
using Microsoft.AspNetCore.Authorization;

namespace FileBridge.Admin.Security;

public static class Policies
{
    public const string View = "View";
    public const string Operate = "Operate";
    public const string Administer = "Administer";
    public const string Approve = "Approve";

    public static void Configure(AuthorizationOptions o)
    {
        // Role-based gating disabled for now: any authenticated Windows user can do everything.
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        o.AddPolicy(View, p => p.RequireAuthenticatedUser());
        o.AddPolicy(Operate, p => p.RequireAuthenticatedUser());
        o.AddPolicy(Administer, p => p.RequireAuthenticatedUser());
        o.AddPolicy(Approve, p => p.RequireAuthenticatedUser());
    }
}
