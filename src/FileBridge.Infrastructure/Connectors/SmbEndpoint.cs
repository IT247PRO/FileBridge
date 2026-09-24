using FileBridge.Core;

namespace FileBridge.Infrastructure.Connectors;

public class SmbFileEntry : IFileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long Length { get; init; }
    public DateTime LastModifiedUtc { get; init; }
    public bool IsDirectory { get; init; }
}

public class SmbEndpoint : IFileEndpoint
{
    private readonly Endpoint _config;

    public SmbEndpoint(Endpoint config)
    {
        _config = config;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        // For SMB/UNC with gMSA or explicit Windows Network credentials
        return Task.CompletedTask;
    }

    public Task<IEnumerable<IFileEntry>> ListFilesAsync(string path, string pattern, CancellationToken ct = default)
    {
        var target = Path.Combine(_config.BasePath ?? "", path);
        if (!Directory.Exists(target))
            return Task.FromResult(Enumerable.Empty<IFileEntry>());

        var di = new DirectoryInfo(target);
        var entries = di.GetFiles(string.IsNullOrWhiteSpace(pattern) ? "*.*" : pattern)
            .Select(f => (IFileEntry)new SmbFileEntry
            {
                Name = f.Name,
                FullPath = f.FullName,
                Length = f.Length,
                LastModifiedUtc = f.LastWriteTimeUtc,
                IsDirectory = false
            });

        return Task.FromResult(entries);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var target = Path.Combine(_config.BasePath ?? "", path);
        Stream stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public async Task UploadAsync(string destinationPath, Stream contentStream, bool overwrite, CancellationToken ct = default)
    {
        var target = Path.Combine(_config.BasePath ?? "", destinationPath);
        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = new FileStream(target, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await contentStream.CopyToAsync(fs, ct);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var target = Path.Combine(_config.BasePath ?? "", path);
        if (File.Exists(target)) File.Delete(target);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        var src = Path.Combine(_config.BasePath ?? "", oldPath);
        var dst = Path.Combine(_config.BasePath ?? "", newPath);
        if (File.Exists(src)) File.Move(src, dst);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var target = Path.Combine(_config.BasePath ?? "", path);
        return Task.FromResult(File.Exists(target) || Directory.Exists(target));
    }

    public Task<bool> IsFileLockedAsync(string path, CancellationToken ct = default)
    {
        var target = Path.Combine(_config.BasePath ?? "", path);
        if (!File.Exists(target)) return Task.FromResult(false);
        try
        {
            using var fs = File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return Task.FromResult(false);
        }
        catch (IOException)
        {
            return Task.FromResult(true);
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
