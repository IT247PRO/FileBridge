using FileBridge.Core;
using FileBridge.Infrastructure;
using FileBridge.Worker;
using FileBridge.Worker.Jobs;
using NLog;
using NLog.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var logFactory = LogManager.LoadConfiguration("nlog.config");
logFactory.Configuration.Variables["connectionString"] = builder.Configuration.GetConnectionString("FileBridge") ?? "";
GlobalDiagnosticsContext.Set("NodeRole", "Worker");
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
builder.Logging.AddNLog();

// Windows Service hosting; runs as a normal console app when launched interactively (e.g. `dotnet run`).
builder.Services.AddWindowsService(o => o.ServiceName = "FileBridge Worker");

builder.Services.AddSingleton<ICurrentUser, SystemUser>();
builder.Services.AddFileBridgeInfrastructure(builder.Configuration, nodeRole: "Worker");
builder.Services.AddFileBridgeEngine(builder.Configuration);

var quartzConnectionString = builder.Configuration.GetConnectionString("FileBridge")
    ?? throw new InvalidOperationException("ConnectionStrings:FileBridge is required.");
var maxConcurrency = builder.Configuration.GetValue("Quartz:MaxConcurrency", 8);

builder.Services.AddQuartz(q =>
{
    q.SchedulerId = "FileBridge-Cluster";
    q.UsePersistentStore(store =>
    {
        store.UseProperties = true;
        store.RetryInterval = TimeSpan.FromSeconds(15);
        store.UseSqlServer(sql => { sql.ConnectionString = quartzConnectionString; sql.TablePrefix = "tblQrtz_"; });
        store.UseSystemTextJsonSerializer();
        store.UseClustering(c =>
        {
            c.CheckinInterval = TimeSpan.FromSeconds(10);
            c.CheckinMisfireThreshold = TimeSpan.FromSeconds(30);
        });
    });
    q.MaxBatchSize = maxConcurrency;

    q.ScheduleJob<ScheduleSyncJob>(t => t.WithIdentity("system-schedule-sync", "system")
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(30).RepeatForever()).StartNow(),
        j => j.WithIdentity("system-schedule-sync", "system").StoreDurably());

    q.ScheduleJob<RunRequestPollerJob>(t => t.WithIdentity("system-request-poller", "system")
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(5).RepeatForever()).StartNow(),
        j => j.WithIdentity("system-request-poller", "system").StoreDurably());

    q.ScheduleJob<RetentionJob>(t => t.WithIdentity("system-retention", "system")
        .WithCronSchedule("0 0 2 * * ?"),
        j => j.WithIdentity("system-retention", "system").StoreDurably());

    q.ScheduleJob<SlaJob>(t => t.WithIdentity("system-sla", "system")
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(60).RepeatForever()).StartNow(),
        j => j.WithIdentity("system-sla", "system").StoreDurably());
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddHostedService<SmbWatcherService>();
builder.Services.AddHostedService<StagingJanitorService>();

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
if (!string.IsNullOrWhiteSpace(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("FileBridge.Worker"))
        .WithTracing(t => t.AddSource(FileBridge.Infrastructure.Engine.Telemetry.Name).AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
        .WithMetrics(m => m.AddMeter(FileBridge.Infrastructure.Engine.Telemetry.Name).AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}

var host = builder.Build();
try
{
    host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>()
        .LogInformation("FileBridge Worker starting on {Node}", Environment.MachineName);
    await host.RunAsync();
}
finally { LogManager.Shutdown(); }
