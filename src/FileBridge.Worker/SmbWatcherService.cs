using FileBridge.Core;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace FileBridge.Worker;

/// <summary>
/// Optional low-latency trigger for SMB/LocalDisk jobs with UseFileWatcher: fires the job a few seconds after
/// a change, on top of its normal schedule. Debounced per job; watcher list refreshed every 2 minutes so a
/// newly-enabled job picks up a watcher without a Worker restart.
/// </summary>
public sealed class SmbWatcherService(IServiceScopeFactory scopes, ISchedulerFactory schedulerFactory, ILogger<SmbWatcherService> log) : BackgroundService
{
    private readonly Dictionary<int, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<int, DateTime> _lastFire = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RefreshAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogWarning(ex, "Watcher refresh failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { }
        }
        foreach (var w in _watchers.Values) w.Dispose();
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FileBridgeDbContext>();
        var jobs = await db.Jobs.AsNoTracking().Include(j => j.SourceEndpoint).Include(j => j.FolderMaps)
            .Where(j => j.IsEnabled && !j.IsPaused && j.UseFileWatcher
                && (j.SourceEndpoint!.EndpointTypeId == EndpointType.Smb || j.SourceEndpoint.EndpointTypeId == EndpointType.LocalDisk))
            .ToListAsync(ct);

        var liveIds = jobs.Select(j => j.Id).ToHashSet();
        foreach (var id in _watchers.Keys.Where(id => !liveIds.Contains(id)).ToList())
        {
            _watchers[id].Dispose();
            _watchers.Remove(id);
        }

        foreach (var job in jobs)
        {
            if (_watchers.ContainsKey(job.Id)) continue;
            var root = job.SourceEndpoint!.BasePath;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = job.FolderMaps.Any(m => m.Recursive),
                    EnableRaisingEvents = true
                };
                watcher.Created += (_, _) => OnChange(job.Id);
                watcher.Renamed += (_, _) => OnChange(job.Id);
                watcher.Changed += (_, _) => OnChange(job.Id);
                _watchers[job.Id] = watcher;
                log.LogInformation("Watching {Root} for job {JobId}", root, job.Id);
            }
            catch (Exception ex) { log.LogWarning(ex, "Could not start a watcher for job {JobId} at {Root}", job.Id, root); }
        }
    }

    private void OnChange(int jobId)
    {
        var now = DateTime.UtcNow;
        lock (_lastFire)
        {
            if (_lastFire.TryGetValue(jobId, out var last) && now - last < Debounce) return;
            _lastFire[jobId] = now;
        }
        _ = FireAsync(jobId);
    }

    private async Task FireAsync(int jobId)
    {
        try
        {
            await Task.Delay(Debounce);
            var scheduler = await schedulerFactory.GetScheduler();
            var data = new JobDataMap { ["jobId"] = jobId.ToString(), ["triggeredBy"] = "watcher", ["manual"] = "false" };
            if (await scheduler.CheckExists(new JobKey($"transfer-{jobId}", "transfer")))
                await scheduler.TriggerJob(new JobKey($"transfer-{jobId}", "transfer"), data);
        }
        catch (Exception ex) { log.LogWarning(ex, "Watcher-triggered run failed for job {JobId}", jobId); }
    }
}
