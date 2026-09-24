namespace FileBridge.Core;

public interface IFileEntry
{
    string Name { get; }
    string FullPath { get; }
    long Length { get; }
    DateTime LastModifiedUtc { get; }
    bool IsDirectory { get; }
}

public interface IFileEndpoint : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<IEnumerable<IFileEntry>> ListFilesAsync(string path, string pattern, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);
    Task UploadAsync(string destinationPath, Stream contentStream, bool overwrite, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default);
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    Task<bool> IsFileLockedAsync(string path, CancellationToken ct = default);
}

public interface ICryptoService
{
    Task<Stream> EncryptAsync(Stream sourceStream, int profileId, CancellationToken ct = default);
    Task<Stream> DecryptAsync(Stream sourceStream, int profileId, CancellationToken ct = default);
    string ComputeSha256(Stream stream);
}

public interface ITransferEngine
{
    Task<TransferHistory> ExecuteJobAsync(int jobId, string correlationId, CancellationToken ct = default);
}
