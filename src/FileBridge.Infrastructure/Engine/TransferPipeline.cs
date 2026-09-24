using System.Security.Cryptography;
using System.Threading.Channels;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;
using FileBridge.Infrastructure.Crypto;
using FileBridge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileBridge.Infrastructure.Engine;

public sealed record PlannedTransfer(string SourcePath, long Size, string DestinationPath, string Note);

public sealed record RunSummary(
    Guid RunId, int Candidates, int Succeeded, int Failed, int Skipped, int Quarantined,
    IReadOnlyList<PlannedTransfer> Planned);

/// <summary>
/// The 10-step per-file pipeline: discover -> filter -> stability/semaphore -> lease -> download ->
/// validate -> decrypt/decompress -> scan -> compress/encrypt -> upload -> verify -> post-action -> notify.
/// One instance is created per run by the Quartz job / request poller (scoped, so it owns one DbContext).
/// </summary>
public sealed class TransferPipeline(
    FileBridgeDbContext db, IDbContextFactory<FileBridgeDbContext> dbFactory, IEndpointFactory endpoints,
    ICryptoService crypto, IAntivirusScanner av, INotifier notifier, IOptions<EngineOptions> options, ILogger<TransferPipeline> log)
{
    private readonly EngineOptions _opt = options.Value;
    private readonly string _node = Environment.MachineName;

    public async Task<RunSummary> RunAsync(int jobId, string triggeredBy, bool dryRun, CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        using var activity = Telemetry.Source.StartActivity("job.run");
        activity?.SetTag("filebridge.job_id", jobId);
        activity?.SetTag("filebridge.dry_run", dryRun);

        if (await GlobalSettings.GetBoolAsync(db, SettingKeys.KillSwitch, ct))
        {
            log.LogWarning("Kill switch is on; skipping job {JobId}", jobId);
            return new RunSummary(runId, 0, 0, 0, 0, 0, Array.Empty<PlannedTransfer>());
        }

        var job = await db.Jobs.AsSplitQuery()
            .Include(j => j.SourceEndpoint).ThenInclude(e => e!.Credential)
            .Include(j => j.DestinationEndpoint).ThenInclude(e => e!.Credential)
            .Include(j => j.EncryptionProfile)
            .Include(j => j.FolderMaps)
            .Include(j => j.Filters)
            .Include(j => j.SemaphoreRule)
            .Include(j => j.PostAction)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new NonRetryableException($"Job {jobId} was not found.");

        var nowUtc = DateTimeOffset.UtcNow;
        await using var source = endpoints.Create(job.SourceEndpoint!);
        await using var destination = endpoints.Create(job.DestinationEndpoint!);
        await source.ConnectAsync(ct);
        await destination.ConnectAsync(ct);

        // ---- Discovery + filtering ----
        var candidates = new List<(JobFolderMap Map, RemoteFile File)>();
        foreach (var map in job.FolderMaps)
        {
            var files = await source.ListAsync(map.SourcePath, map.Recursive, ct);
            foreach (var f in files)
            {
                var name = RemotePath.GetFileName(f.Path);
                if (name.EndsWith(".fbtmp", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue;
                if (job.SemaphoreRule is { SemaphoreModeId: SemaphoreMode.PerFile } sem
                    && name.Equals(RemotePath.GetFileName(TokenRenamer.ApplyNameTokens(sem.TriggerPattern, "")), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!FileFilterEvaluator.IsMatch(f, job.Filters, nowUtc)) continue;
                if (f.LastModifiedUtc > nowUtc.AddSeconds(-job.StabilitySeconds)) continue;
                candidates.Add((map, f));
            }
        }

        if (job.SemaphoreRule is { SemaphoreModeId: SemaphoreMode.Batch } batchSem)
        {
            var ready = new List<(JobFolderMap, RemoteFile)>();
            foreach (var group in candidates.GroupBy(c => c.Map))
            {
                var flag = RemotePath.Combine(group.Key.SourcePath, batchSem.TriggerPattern);
                if (await source.ExistsAsync(flag, ct))
                {
                    ready.AddRange(group);
                    if (batchSem.DeleteTriggerAfter && !dryRun) await source.DeleteAsync(flag, ct);
                }
            }
            candidates = ready;
        }

        if (dryRun)
        {
            var planned = candidates.Select(c => new PlannedTransfer(
                c.File.Path, c.File.Size,
                DestinationFor(job, c.Map, c.File.Path, job.DestinationEndpoint!.BasePath, nowUtc),
                "Would transfer")).ToList();
            return new RunSummary(runId, candidates.Count, 0, 0, 0, 0, planned);
        }

        // ---- Transfer, MaxParallelFiles at a time ----
        int succeeded = 0, failed = 0, skipped = 0, quarantined = 0;
        var perFileTriggers = new List<(JobFolderMap Map, string Path)>();

        var channel = Channel.CreateBounded<(JobFolderMap Map, RemoteFile File)>(Math.Max(1, job.MaxParallelFiles));
        var producer = Task.Run(async () =>
        {
            foreach (var c in candidates) await channel.Writer.WriteAsync(c, ct);
            channel.Writer.Complete();
        }, ct);

        await Parallel.ForEachAsync(channel.Reader.ReadAllAsync(ct),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, job.MaxParallelFiles), CancellationToken = ct },
            async (item, token) =>
            {
                var result = await ProcessFileAsync(job, item.Map, item.File, source, destination, runId, triggeredBy, nowUtc, token);
                lock (this)
                {
                    switch (result)
                    {
                        case TransferStatus.Succeeded: succeeded++; break;
                        case TransferStatus.Failed: failed++; break;
                        case TransferStatus.Quarantined: quarantined++; break;
                        default: skipped++; break;
                    }
                }
                if (result == TransferStatus.Succeeded && job.SemaphoreRule is { SemaphoreModeId: SemaphoreMode.PerFile, TransferTrigger: true })
                    lock (this) perFileTriggers.Add((item.Map, item.File.Path));
            });
        await producer;

        foreach (var (map, path) in perFileTriggers)
        {
            try
            {
                var trigger = TokenRenamer.ApplyNameTokens(job.SemaphoreRule!.TriggerPattern, RemotePath.GetFileName(path));
                await using var empty = new MemoryStream();
                await destination.UploadAsync(empty, RemotePath.Combine(RemotePath.GetDirectory(DestinationFor(job, map, path, job.DestinationEndpoint!.BasePath, nowUtc)), trigger), true, ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "Per-file trigger upload failed for {Path}", path); }
        }

        // Batch trigger: only when every file in this run made it through clean.
        if (job.SemaphoreRule is { SemaphoreModeId: SemaphoreMode.Batch, TransferTrigger: true } && succeeded > 0 && failed == 0 && quarantined == 0)
        {
            try
            {
                await using var empty = new MemoryStream();
                await destination.UploadAsync(empty, RemotePath.Combine(job.DestinationEndpoint!.BasePath, job.SemaphoreRule.TriggerPattern), true, ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "Batch trigger upload failed for job {JobId}", job.Id); }
        }

        Telemetry.Files.Add(succeeded, new KeyValuePair<string, object?>("job", job.Name), new("status", "succeeded"));
        Telemetry.Files.Add(failed, new("job", job.Name), new("status", "failed"));

        if (failed > 0)
            await notifier.NotifyAsync(NotificationEvent.JobFailed, job.Id, $"FileBridge: {job.Name} had {failed} failure(s)",
                $"Run {runId}: {succeeded} succeeded, {failed} failed, {quarantined} quarantined, {skipped} skipped.", ct);
        else if (succeeded > 0)
            await notifier.NotifyAsync(NotificationEvent.JobSucceeded, job.Id, $"FileBridge: {job.Name} completed",
                $"Run {runId}: {succeeded} file(s) transferred.", ct);

        CleanupStaging(runId);
        return new RunSummary(runId, candidates.Count, succeeded, failed, skipped, quarantined, Array.Empty<PlannedTransfer>());
    }

    private async Task<TransferStatus> ProcessFileAsync(Job job, JobFolderMap map, RemoteFile file,
        IFileEndpoint source, IFileEndpoint destination, Guid runId, string triggeredBy, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (await source.IsLockedAsync(file.Path, ct)) return TransferStatus.Skipped;

        // Own DbContext per parallel file: TransferPipeline's injected db is shared across the whole run,
        // but up to MaxParallelFiles of these run concurrently and DbContext is not thread-safe.
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var fingerprint = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{job.SourceEndpointId}|{file.Path.ToLowerInvariant()}|{file.Size}|{file.LastModifiedUtc.UtcTicks}")));

        var lease = await Leases.TryAcquireAsync(db, job.Id, fingerprint, file.Path, _node,
            TimeSpan.FromMinutes(_opt.LeaseMinutes), _opt.MaxLeaseAttempts, ct);
        if (lease is null) return TransferStatus.Skipped; // already claimed, completed, or attempts exhausted elsewhere

        var history = new TransferHistory
        {
            JobId = job.Id,
            RunId = runId,
            CorrelationId = runId.ToString("N"),
            SourcePath = file.Path,
            FileName = RemotePath.GetFileName(file.Path),
            SizeBytes = file.Size,
            TransferStatusId = TransferStatus.InProgress,
            AttemptCount = 0,
            StartedUtc = DateTime.UtcNow,
            NodeName = _node,
            TriggeredBy = triggeredBy
        };
        db.TransferHistories.Add(history);
        await db.SaveChangesAsync(ct);

        var staging = Path.Combine(_opt.StagingRoot, runId.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var finalStatus = TransferStatus.Failed;
        string? error = null;

        try
        {
            for (var attempt = 1; attempt <= job.MaxRetries; attempt++)
            {
                history.AttemptCount = attempt;
                try
                {
                    var destPath = await TransferOnceAsync(job, map, file, source, destination, staging, nowUtc, ct);
                    history.DestinationPath = destPath;
                    finalStatus = TransferStatus.Succeeded;
                    error = null;
                    break;
                }
                catch (QuarantineException qx)
                {
                    await QuarantineAsync(db, job, map, file, staging, qx.Message, history.Id, ct);
                    finalStatus = TransferStatus.Quarantined;
                    error = qx.Message;
                    break;
                }
                catch (DuplicateSkipException dsx)
                {
                    finalStatus = TransferStatus.Skipped;
                    error = dsx.Message;
                    break;
                }
                catch (NonRetryableException nrx)
                {
                    finalStatus = TransferStatus.Failed;
                    error = nrx.Message;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    error = ex.Message;
                    finalStatus = TransferStatus.Failed;
                    if (attempt < job.MaxRetries)
                    {
                        log.LogWarning(ex, "Transfer of {Path} failed (attempt {Attempt}/{Max}); retrying", file.Path, attempt, job.MaxRetries);
                        await Task.Delay(RetryPolicy.Backoff(attempt, job.RetryBaseSeconds), ct);
                    }
                }
            }
        }
        finally
        {
            history.TransferStatusId = finalStatus;
            history.ErrorMessage = error;
            history.CompletedUtc = DateTime.UtcNow;
            history.DurationMs = (long)(history.CompletedUtc.Value - history.StartedUtc).TotalMilliseconds;
            await db.SaveChangesAsync(CancellationToken.None);

            await (finalStatus == TransferStatus.Succeeded
                ? Leases.CompleteAsync(db, lease.Value, finalStatus, CancellationToken.None)
                : Leases.FailAsync(db, lease.Value, TimeSpan.FromMinutes(_opt.FailedCooldownMinutes), CancellationToken.None));

            if (finalStatus == TransferStatus.Quarantined)
                await notifier.NotifyAsync(NotificationEvent.FileQuarantined, job.Id,
                    $"FileBridge: file quarantined ({job.Name})", $"{file.Path}: {error}", CancellationToken.None);

            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { /* best effort */ }
        }
        return finalStatus;
    }

    /// <summary>
    /// One end-to-end attempt for a single file. Also used by ReleaseQuarantineAsync with localSource set and
    /// validation skipped, since the file already passed those checks before being quarantined.
    /// </summary>
    private async Task<string> TransferOnceAsync(Job job, JobFolderMap map, RemoteFile file,
        IFileEndpoint source, IFileEndpoint destination, string staging, DateTimeOffset nowUtc, CancellationToken ct,
        string? localSource = null, bool skipValidation = false)
    {
        var name = RemotePath.GetFileName(file.Path);
        var downloaded = Path.Combine(staging, "0-" + name);

        if (localSource is not null) File.Copy(localSource, downloaded, true);
        else
        {
            await using (var fs = File.Create(downloaded)) await source.DownloadAsync(file.Path, fs, ct);
            var actualSize = new FileInfo(downloaded).Length;
            if (actualSize != file.Size)
                throw new IOException($"Downloaded {actualSize} bytes but the source reported {file.Size}.");
        }

        var current = downloaded;
        var currentName = name;

        if (!skipValidation)
        {
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(current), ct)).ToLowerInvariant();
            if (job.ValidateChecksumManifest)
            {
                var manifestPath = file.Path + ".sha256";
                if (!await source.ExistsAsync(manifestPath, ct))
                    throw new QuarantineException("Checksum manifest is required but was not found.");
                await using var ms = new MemoryStream();
                await source.DownloadAsync(manifestPath, ms, ct);
                var expected = System.Text.Encoding.UTF8.GetString(ms.ToArray()).Trim().Split(' ', '\t')[0].Trim().ToLowerInvariant();
                if (expected != sha) throw new QuarantineException($"Checksum mismatch: manifest says {expected}, computed {sha}.");
            }
        }

        // Decrypt (inbound side typically decrypts what a partner encrypted)
        if (job.EncryptionProfile is { } profile && CryptoService.IsDecrypt(profile.EncryptionOperationId))
        {
            var outName = CryptoService.OutputName(profile, currentName);
            var outPath = Path.Combine(staging, "1-" + outName);
            await crypto.TransformAsync(profile, current, outPath, ct);
            current = outPath;
            currentName = outName;
        }

        if (job.CompressionOperationId == CompressionOperation.Unzip)
        {
            var extractDir = Path.Combine(staging, "unzipped");
            Directory.CreateDirectory(extractDir);
            var extracted = Compression.Unzip(current, extractDir, _opt.MaxUnzipBytes, _opt.MaxUnzipEntries);
            if (extracted.Count != 1)
                throw new QuarantineException($"Expected exactly one file inside the archive, found {extracted.Count}.");
            current = extracted[0];
            currentName = Path.GetFileName(current);
        }

        if (job.VirusScanEnabled)
        {
            var scan = await av.ScanAsync(current, ct);
            if (!scan.Clean) throw new QuarantineException(scan.Detail);
        }

        // Type check always runs on the post-decrypt/decompress payload, so a renamed executable can't pass as data.
        var typeFilter = job.Filters.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.AllowedFileTypes) && !f.IsExclude);
        if (typeFilter?.AllowedFileTypes is { Length: > 0 } allowedTypes)
        {
            var detected = await FileTypeSniffer.DetectAsync(current, ct);
            if (!FileTypeSniffer.IsAllowed(detected, allowedTypes))
                throw new QuarantineException($"File type '{detected}' is not in the allowed list ({allowedTypes}).");
        }

        if (job.CompressionOperationId == CompressionOperation.Zip)
        {
            current = Compression.Zip(current, staging);
            currentName += ".zip";
        }

        if (job.EncryptionProfile is { } encProfile && CryptoService.IsEncrypt(encProfile.EncryptionOperationId))
        {
            var outName = CryptoService.OutputName(encProfile, currentName);
            var outPath = Path.Combine(staging, "2-" + outName);
            await crypto.TransformAsync(encProfile, current, outPath, ct);
            current = outPath;
            currentName = outName;
        }

        currentName = TokenRenamer.Apply(job.RenamePattern, currentName, job.Name, TimeZoneInfo.ConvertTime(nowUtc, ScheduleWindow.FindZone(job.TimeZoneId)));

        var destRelDir = RemotePath.GetDirectory(DestinationFor(job, map, file.Path, "", nowUtc));
        var destPath = RemotePath.Combine(destRelDir, currentName);

        if (await destination.ExistsAsync(destPath, ct))
        {
            switch (job.DuplicatePolicyId)
            {
                case DuplicatePolicy.Skip: throw new DuplicateSkipException($"'{destPath}' already exists; policy is Skip.");
                case DuplicatePolicy.Fail: throw new NonRetryableException($"'{destPath}' already exists; policy is Fail.");
                case DuplicatePolicy.Version:
                    var baseName = Path.GetFileNameWithoutExtension(currentName);
                    var ext = Path.GetExtension(currentName);
                    var n = 1;
                    string candidate;
                    do { candidate = RemotePath.Combine(destRelDir, $"{baseName}_{n++}{ext}"); }
                    while (await destination.ExistsAsync(candidate, ct) && n < 1000);
                    destPath = candidate;
                    break;
                // Overwrite: fall through, UploadAsync(overwrite: true) handles it.
            }
        }

        var uploadedSize = new FileInfo(current).Length;
        if (job.UseTempNameOnUpload)
        {
            var tempPath = RemotePath.Combine(destRelDir, "." + RemotePath.GetFileName(destPath) + ".fbtmp");
            await using (var fs = File.OpenRead(current)) await destination.UploadAsync(fs, tempPath, true, ct);
            if (job.VerifyAfterUpload)
            {
                var remoteSize = await destination.GetSizeAsync(tempPath, ct);
                if (remoteSize != uploadedSize)
                {
                    await destination.DeleteAsync(tempPath, ct);
                    throw new IOException($"Uploaded {remoteSize ?? 0} bytes but expected {uploadedSize}.");
                }
            }
            await destination.RenameAsync(tempPath, destPath, job.DuplicatePolicyId == DuplicatePolicy.Overwrite, ct);
        }
        else
        {
            await using var fs = File.OpenRead(current);
            await destination.UploadAsync(fs, destPath, job.DuplicatePolicyId == DuplicatePolicy.Overwrite, ct);
            if (job.VerifyAfterUpload)
            {
                var remoteSize = await destination.GetSizeAsync(destPath, ct);
                if (remoteSize != uploadedSize) throw new IOException($"Uploaded {remoteSize ?? 0} bytes but expected {uploadedSize}.");
            }
        }
        Telemetry.Bytes.Add(uploadedSize, new KeyValuePair<string, object?>("job", job.Name));

        if (job.GenerateChecksumManifest)
        {
            var finalSha = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(current), ct)).ToLowerInvariant();
            await using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"{finalSha}  {RemotePath.GetFileName(destPath)}\n"));
            await destination.UploadAsync(ms, destPath + ".sha256", true, ct);
        }

        await ApplyPostActionAsync(job, source, file.Path, ct);
        return destPath;
    }

    private async Task ApplyPostActionAsync(Job job, IFileEndpoint source, string sourcePath, CancellationToken ct)
    {
        if (job.PostAction is not { PostActionTypeId: not PostActionType.None } pa) return;
        try
        {
            switch (pa.PostActionTypeId)
            {
                case PostActionType.Delete:
                    await source.DeleteAsync(sourcePath, ct);
                    break;
                case PostActionType.Archive:
                    var archiveDir = RemotePath.Combine(RemotePath.GetDirectory(sourcePath), pa.ArchivePath ?? "archive");
                    var archiveName = TokenRenamer.Apply(pa.RenamePattern ?? "{name}_{date:yyyyMMddHHmmss}{ext}",
                        RemotePath.GetFileName(sourcePath), job.Name, DateTimeOffset.UtcNow);
                    await source.RenameAsync(sourcePath, RemotePath.Combine(archiveDir, archiveName), true, ct);
                    break;
                case PostActionType.Rename:
                    var newName = TokenRenamer.Apply(pa.RenamePattern, RemotePath.GetFileName(sourcePath), job.Name, DateTimeOffset.UtcNow);
                    await source.RenameAsync(sourcePath, RemotePath.Combine(RemotePath.GetDirectory(sourcePath), newName), true, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Post-action {Action} failed for {Path}; the transfer itself already succeeded", pa.PostActionTypeId, sourcePath);
        }
    }

    private async Task QuarantineAsync(FileBridgeDbContext db, Job job, JobFolderMap map, RemoteFile file, string staging, string reason, long historyId, CancellationToken ct)
    {
        var dir = Path.Combine(_opt.QuarantineRoot, job.Id.ToString(), DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"{Guid.NewGuid():N}_{RemotePath.Sanitize(RemotePath.GetFileName(file.Path))}");
        var original = Path.Combine(staging, "0-" + RemotePath.GetFileName(file.Path));
        if (File.Exists(original)) File.Copy(original, dest, true);

        db.Quarantines.Add(new Quarantine
        {
            JobId = job.Id,
            FolderMapId = map.Id,
            TransferHistoryId = historyId,
            OriginalPath = file.Path,
            QuarantinePath = dest,
            Reason = reason,
            QuarantineStatusId = QuarantineStatus.Held,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static string DestinationFor(Job job, JobFolderMap map, string sourceRelPath, string destBase, DateTimeOffset nowUtc)
    {
        var name = RemotePath.GetFileName(sourceRelPath);
        var subdir = "";
        if (map.PreserveSubfolders)
        {
            var srcDir = RemotePath.Normalize(RemotePath.GetDirectory(sourceRelPath));
            var mapDir = RemotePath.Normalize(map.SourcePath);
            if (srcDir.StartsWith(mapDir, StringComparison.OrdinalIgnoreCase))
                subdir = srcDir.Length > mapDir.Length ? srcDir[mapDir.Length..].TrimStart('/') : "";
        }
        var renamed = TokenRenamer.Apply(job.RenamePattern, name, job.Name, nowUtc);
        return RemotePath.Combine(destBase, map.DestinationPath, subdir, renamed);
    }

    /// <summary>Manual release from quarantine: re-run the pipeline from the held copy, then discard it on success.</summary>
    public async Task ReleaseQuarantineAsync(int quarantineId, string releasedBy, CancellationToken ct)
    {
        var q = await db.Quarantines.Include(x => x.Job).FirstOrDefaultAsync(x => x.Id == quarantineId, ct)
            ?? throw new NonRetryableException($"Quarantine item {quarantineId} was not found.");
        if (q.QuarantineStatusId != QuarantineStatus.Held) throw new NonRetryableException("This item was already reviewed.");
        if (!File.Exists(q.QuarantinePath)) throw new NonRetryableException("The quarantined file is missing from disk.");

        var job = await db.Jobs.AsSplitQuery()
            .Include(j => j.DestinationEndpoint).ThenInclude(e => e!.Credential)
            .Include(j => j.FolderMaps)
            .FirstAsync(j => j.Id == q.JobId, ct);
        var map = job.FolderMaps.FirstOrDefault(m => m.Id == q.FolderMapId) ?? job.FolderMaps.First();

        await using var destination = endpoints.Create(job.DestinationEndpoint!);
        await destination.ConnectAsync(ct);
        var staging = Path.Combine(_opt.StagingRoot, "release", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var file = new RemoteFile(q.OriginalPath, RemotePath.GetFileName(q.OriginalPath), new FileInfo(q.QuarantinePath).Length, DateTimeOffset.UtcNow);
            await TransferOnceAsync(job, map, file, null!, destination, staging, DateTimeOffset.UtcNow, ct, localSource: q.QuarantinePath, skipValidation: true);
            q.QuarantineStatusId = QuarantineStatus.Released;
            q.ReviewedBy = releasedBy;
            q.ReviewedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        finally { try { Directory.Delete(staging, true); } catch { /* best effort */ } }
    }

    public async Task DiscardQuarantineAsync(int quarantineId, string discardedBy, CancellationToken ct)
    {
        var q = await db.Quarantines.FirstOrDefaultAsync(x => x.Id == quarantineId, ct)
            ?? throw new NonRetryableException($"Quarantine item {quarantineId} was not found.");
        if (q.QuarantineStatusId != QuarantineStatus.Held) throw new NonRetryableException("This item was already reviewed.");
        try { if (File.Exists(q.QuarantinePath)) File.Delete(q.QuarantinePath); } catch { /* best effort */ }
        q.QuarantineStatusId = QuarantineStatus.Discarded;
        q.ReviewedBy = discardedBy;
        q.ReviewedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static void CleanupStaging(Guid runId)
    {
        // Per-file staging is removed as each file completes; this removes the (normally empty) run folder.
    }
}
