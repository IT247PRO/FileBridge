using FileBridge.Core.Entities;

namespace FileBridge.Core;

/// <summary>A file on an endpoint. Path is endpoint-relative with '/' separators.</summary>
public sealed record RemoteFile(string Path, string Name, long Size, DateTimeOffset LastModifiedUtc);

public sealed record ConnectionTestResult(bool Success, string Message, string? ObservedHostKey = null);

/// <summary>Protocol-neutral endpoint. All paths are relative to the endpoint BasePath.</summary>
public interface IFileEndpoint : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task<IReadOnlyList<RemoteFile>> ListAsync(string folder, bool recursive, CancellationToken ct);
    Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct);
    Task<bool> ExistsAsync(string path, CancellationToken ct);
    Task<long?> GetSizeAsync(string path, CancellationToken ct);
    Task<bool> IsLockedAsync(string path, CancellationToken ct);
    Task DownloadAsync(string path, Stream destination, CancellationToken ct);
    Task UploadAsync(Stream source, string path, bool overwrite, CancellationToken ct);
    Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    Task<ConnectionTestResult> TestAsync(CancellationToken ct);
}

public interface IEndpointFactory { IFileEndpoint Create(Endpoint endpoint); }

public interface ISecretProtector
{
    string? Protect(string? plaintext);
    string? Unprotect(string? protectedValue);
}

public interface ICurrentUser { string Name { get; } }

public interface ICryptoService
{
    /// <summary>Applies the profile's operation to inputPath, writes to outputPath.</summary>
    Task TransformAsync(EncryptionProfile profile, string inputPath, string outputPath, CancellationToken ct);
}

public sealed record ScanResult(bool Clean, string Detail);

public interface IAntivirusScanner { Task<ScanResult> ScanAsync(string path, CancellationToken ct); }

public interface INotifier
{
    Task NotifyAsync(NotificationEvent evt, int? jobId, string subject, string body, CancellationToken ct);
}

/// <summary>File failed a content check (AV, type, checksum). Held for review, never retried.</summary>
public sealed class QuarantineException(string message) : Exception(message);

/// <summary>Destination exists and policy is Skip.</summary>
public sealed class DuplicateSkipException(string message) : Exception(message);

/// <summary>Failure that retrying will not fix (config error, duplicate with Fail policy).</summary>
public sealed class NonRetryableException(string message, Exception? inner = null) : Exception(message, inner);
