using FileBridge.Admin.Security;
using FileBridge.Core;
using FileBridge.Infrastructure;
using FileBridge.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var logFactory = LogManager.LoadConfiguration("nlog.config");
logFactory.Configuration.Variables["connectionString"] = builder.Configuration.GetConnectionString("FileBridge") ?? "";
GlobalDiagnosticsContext.Set("NodeRole", "Admin");
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// ---- Auth: Windows/Negotiate behind IIS (Anonymous + Windows auth both enabled at the site level) ----
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(Policies.Configure);
builder.Services.AddTransient<IClaimsTransformation, RoleClaimsTransformation>();
builder.Services.AddHttpContextAccessor();

// ---- App services ----
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddFileBridgeInfrastructure(builder.Configuration, nodeRole: "Admin");
builder.Services.AddScoped<FileBridge.Admin.Services.JobService>();
builder.Services.AddScoped<FileBridge.Admin.Services.RequestService>();

builder.Services.AddControllersWithViews(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("FileBridge")!, name: "sql", tags: ["ready"]);

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("FileBridge.Admin"))
        .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
        .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}

var app = builder.Build();

// ---- Security headers + strict CSP (no inline script; site.js + libman-vendored Bootstrap only) ----
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options", "DENY");
    ctx.Response.Headers.Append("Referrer-Policy", "same-origin");
    ctx.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

// Apply EF migrations / ensure schema exists is handled by the db/*.sql scripts (see README); the app does
// not run migrations at startup so multiple Admin nodes never race a schema change.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FileBridgeDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
    try { await db.Database.CanConnectAsync(); }
    catch (Exception ex) { logger.LogWarning(ex, "Could not reach the database at startup; will retry on first request."); }
}

try
{
    app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>()
        .LogInformation("FileBridge Admin starting on {Node}", Environment.MachineName);
    app.Run();
}
finally { LogManager.Shutdown(); }
