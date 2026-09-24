using System.Runtime.InteropServices;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;

namespace FileBridge.Infrastructure.Connectors;

/// <summary>
/// SMB via UNC paths. Default: runs as the Worker's gMSA (no stored password).
/// When a credential is attached, the share is mapped with WNetAddConnection2 (ref-counted across parallel lanes).
/// Also serves LocalDisk endpoints.
/// </summary>
public sealed class SmbEndpoint(Endpoint endpoint, string? user, string? domain, string? password) : IFileEndpoint
{
    private readonly string _root = endpoint.BasePath.TrimEnd('\\', '/');
    private string? _mappedShare;

    private string Full(string rel)
    {
        var full = Path.GetFullPath(Path.Combine(_root, RemotePath.Normalize(rel).Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(_root);
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new NonRetryableException($"Path '{rel}' escapes the endpoint root.");
        return full;
    }

    private string Rel(string full) => Path.GetRelativePath(_root, full).Replace(Path.DirectorySeparatorChar, '/');

    public Task ConnectAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(user) && OperatingSystem.IsWindows() && _root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var share = ShareConnections.ShareRoot(_root);
            var account = string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}";
            ShareConnections.Acquire(share, account, password);
            _mappedShare = share;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RemoteFile>> ListAsync(string folder, bool recursive, CancellationToken ct)
    {
        var dir = Full(folder);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<RemoteFile>>(Array.Empty<RemoteFile>());
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Temporary
        };
        IReadOnlyList<RemoteFile> files = new DirectoryInfo(dir).EnumerateFiles("*", opts)
            .Select(f => new RemoteFile(Rel(f.FullName), f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToList();
        return Task.FromResult(files);
    }

    public Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct)
    {
        var dir = Full(folder);
        IReadOnlyList<string> result = Directory.Exists(dir)
            ? new DirectoryInfo(dir).EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true })
                .Select(d => Rel(d.FullName)).OrderBy(x => x).ToList()
            : Array.Empty<string>();
        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(File.Exists(Full(path)));

    public Task<long?> GetSizeAsync(string path, CancellationToken ct)
    {
        var fi = new FileInfo(Full(path));
        return Task.FromResult(fi.Exists ? fi.Length : (long?)null);
    }

    public Task<bool> IsLockedAsync(string path, CancellationToken ct)
    {
        try
        {
            using var _ = new FileStream(Full(path), FileMode.Open, FileAccess.Read, FileShare.None);
            return Task.FromResult(false);
        }
        catch (IOException) { return Task.FromResult(true); }
    }

    public async Task DownloadAsync(string path, Stream destination, CancellationToken ct)
    {
        await using var fs = new FileStream(Full(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await fs.CopyToAsync(destination, ct);
    }

    public async Task UploadAsync(Stream source, string path, bool overwrite, CancellationToken ct)
    {
        var full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = new FileStream(full, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(fs, ct);
        await fs.FlushAsync(ct);
    }

    public Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct)
    {
        var target = Full(to);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(Full(from), target, overwrite);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        var full = Full(path);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public async Task<ConnectionTestResult> TestAsync(CancellationToken ct)
    {
        try
        {
            await ConnectAsync(ct);
            if (!Directory.Exists(_root)) return new(false, $"Folder not found or not accessible: {_root}");
            var probe = Path.Combine(_root, $".filebridge-probe-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "probe", ct);
            File.Delete(probe);
            return new(true, $"Read and write access confirmed on {_root} as {Environment.UserDomainName}\\{Environment.UserName}.");
        }
        catch (UnauthorizedAccessException) { return new(false, $"Read access OK, but write access was denied on {_root}."); }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public ValueTask DisposeAsync()
    {
        if (_mappedShare is not null) ShareConnections.Release(_mappedShare);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Reference-counted WNet connections so parallel lanes don't disconnect each other.</summary>
internal static class ShareConnections
{
    private static readonly Dictionary<string, int> RefCounts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static string ShareRoot(string unc)
    {
        var parts = unc.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : unc;
    }

    public static void Acquire(string share, string account, string? password)
    {
        lock (Gate)
        {
            if (RefCounts.TryGetValue(share, out var n)) { RefCounts[share] = n + 1; return; }
            var rc = Native.Connect(share, account, password);
            // 1219: already connected with other credentials (e.g. service account) - usable as-is.
            if (rc != 0 && rc != 1219 && rc != 85)
                throw new IOException($"Could not connect to {share} as {account} (Win32 error {rc}).");
            RefCounts[share] = 1;
        }
    }

    public static void Release(string share)
    {
        lock (Gate)
        {
            if (!RefCounts.TryGetValue(share, out var n)) return;
            if (n > 1) { RefCounts[share] = n - 1; return; }
            RefCounts.Remove(share);
            Native.Disconnect(share);
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class NetResource
        {
            public int Scope;
            public int Type = 1; // RESOURCETYPE_DISK
            public int DisplayType;
            public int Usage;
            public string? LocalName;
            public string? RemoteName;
            public string? Comment;
            public string? Provider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(NetResource resource, string? password, string? username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        public static int Connect(string share, string user, string? password) =>
            WNetAddConnection2(new NetResource { RemoteName = share }, password, user, 0);

        public static void Disconnect(string share) => WNetCancelConnection2(share, 0, true);
    }
}
