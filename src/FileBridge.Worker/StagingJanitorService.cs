using FileBridge.Infrastructure;
using Microsoft.Extensions.Options;

namespace FileBridge.Worker;

/// <summary>Removes orphaned per-run staging folders left behind by a crash, every 15 minutes.</summary>
public sealed class StagingJanitorService(IOptions<EngineOptions> options, ILogger<StagingJanitorService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Sweep(); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { log.LogWarning(ex, "Staging cleanup failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(15), ct); } catch (OperationCanceledException) { }
        }
    }

    private void Sweep()
    {
        var root = options.Value.StagingRoot;
        if (!Directory.Exists(root)) return;
        var cutoff = DateTime.UtcNow.AddHours(-4);
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                {
                    Directory.Delete(dir, true);
                    log.LogInformation("Removed orphaned staging folder {Dir}", dir);
                }
            }
            catch (Exception ex) { log.LogDebug(ex, "Could not remove {Dir}", dir); }
        }
    }
}
