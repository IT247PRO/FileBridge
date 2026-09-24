using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FileBridge.Worker";
});

builder.Services.AddDbContext<FileBridgeDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("FileBridgeDb"));
});

builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(s =>
    {
        s.UseProperties = true;
        s.UseSqlServer(builder.Configuration.GetConnectionString("FileBridgeDb")!);
        s.UseJsonSerializer();
        s.UseClustering();
        s.TablePrefix = "tblQrtz_";
    });
});

builder.Services.AddQuartzHostedService(opt =>
{
    opt.WaitForJobsToComplete = true;
});

var host = builder.Build();
host.Run();
