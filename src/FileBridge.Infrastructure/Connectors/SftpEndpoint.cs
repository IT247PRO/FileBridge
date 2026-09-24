using System.Text;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace FileBridge.Infrastructure.Connectors;

/// <summary>SFTP connector for remote/partner endpoints reachable only that way. Host key is pinned; unknown keys are refused.</summary>
public sealed class SftpEndpoint : IFileEndpoint
{
    private readonly Endpoint _endpoint;
    private readonly SftpClient _client;
    private readonly string _base;
    private string? _observedHostKey;

    public SftpEndpoint(Endpoint endpoint, string user, string? password, string? privateKey, string? passphrase)
    {
        _endpoint = endpoint;
        _base = "/" + RemotePath.Normalize(endpoint.BasePath);
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(privateKey))
        {
            var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKey));
            var keyFile = string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(keyStream) : new PrivateKeyFile(keyStream, passphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(user, keyFile));
        }
        if (!string.IsNullOrEmpty(password)) methods.Add(new PasswordAuthenticationMethod(user, password));
        if (methods.Count == 0) throw new NonRetryableException($"Endpoint '{endpoint.Name}' has no SFTP password or private key.");

        var info = new ConnectionInfo(endpoint.Host ?? throw new NonRetryableException("SFTP host is required."),
            endpoint.Port ?? 22, user, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(endpoint.TimeoutSeconds)
        };
        _client = new SftpClient(info) { OperationTimeout = TimeSpan.FromSeconds(Math.Max(endpoint.TimeoutSeconds, 30)) };
        _client.HostKeyReceived += (_, e) =>
        {
            _observedHostKey = e.FingerPrintSHA256;
            e.CanTrust = !string.IsNullOrWhiteSpace(_endpoint.HostKeyFingerprint)
                         && Normalize(_endpoint.HostKeyFingerprint) == Normalize(e.FingerPrintSHA256);
        };
    }

    private static string Normalize(string fp) =>
        fp.Trim().Replace("SHA256:", "", StringComparison.OrdinalIgnoreCase).TrimEnd('=');

    private string Full(string rel)
    {
        var p = RemotePath.Normalize(rel);
        if (p.Split('/').Contains("..")) throw new NonRetryableException($"Path '{rel}' escapes the endpoint root.");
        return p.Length == 0 ? _base : (_base.TrimEnd('/') + "/" + p);
    }

    private string Rel(string full) => RemotePath.Normalize(full.StartsWith(_base, StringComparison.Ordinal) ? full[_base.Length..] : full);

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (!_client.IsConnected) await _client.ConnectAsync(ct);
    }

    public async Task<IReadOnlyList<RemoteFile>> ListAsync(string folder, bool recursive, CancellationToken ct)
    {
        var result = new List<RemoteFile>();
        await WalkAsync(Full(folder), recursive, result, ct);
        return result;
    }

    private async Task WalkAsync(string dir, bool recursive, List<RemoteFile> into, CancellationToken ct)
    {
        if (!_client.Exists(dir)) return;
        await foreach (var f in _client.ListDirectoryAsync(dir, ct))
        {
            if (f.Name is "." or "..") continue;
            if (f.IsDirectory) { if (recursive) await WalkAsync(f.FullName, true, into, ct); }
            else if (f.IsRegularFile)
                into.Add(new RemoteFile(Rel(f.FullName), f.Name, f.Length,
                    new DateTimeOffset(DateTime.SpecifyKind(f.LastWriteTimeUtc, DateTimeKind.Utc))));
        }
    }

    public async Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct)
    {
        var dir = Full(folder);
        var result = new List<string>();
        if (!_client.Exists(dir)) return result;
        await foreach (var f in _client.ListDirectoryAsync(dir, ct))
            if (f.IsDirectory && f.Name is not "." and not "..") result.Add(Rel(f.FullName));
        return result.OrderBy(x => x).ToList();
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(_client.Exists(Full(path)));

    public Task<long?> GetSizeAsync(string path, CancellationToken ct)
    {
        try { return Task.FromResult<long?>(_client.Get(Full(path)).Length); }
        catch (SftpPathNotFoundException) { return Task.FromResult<long?>(null); }
    }

    public Task<bool> IsLockedAsync(string path, CancellationToken ct) => Task.FromResult(false);

    public Task DownloadAsync(string path, Stream destination, CancellationToken ct) =>
        Task.Run(() => _client.DownloadFile(Full(path), destination), ct);

    public Task UploadAsync(Stream source, string path, bool overwrite, CancellationToken ct) => Task.Run(() =>
    {
        EnsureDirectory(RemotePath.GetDirectory(RemotePath.Normalize(path)));
        _client.UploadFile(source, Full(path), overwrite);
    }, ct);

    private void EnsureDirectory(string rel)
    {
        var current = _base.TrimEnd('/');
        foreach (var part in RemotePath.Normalize(rel).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + part;
            if (!_client.Exists(current)) _client.CreateDirectory(current);
        }
    }

    public Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct) => Task.Run(() =>
    {
        EnsureDirectory(RemotePath.GetDirectory(to));
        var target = Full(to);
        if (overwrite && _client.Exists(target)) _client.DeleteFile(target);
        _client.RenameFile(Full(from), target);
    }, ct);

    public Task DeleteAsync(string path, CancellationToken ct) => Task.Run(() =>
    {
        var full = Full(path);
        if (_client.Exists(full)) _client.DeleteFile(full);
    }, ct);

    public async Task<ConnectionTestResult> TestAsync(CancellationToken ct)
    {
        try
        {
            await ConnectAsync(ct);
            var exists = _client.Exists(_base);
            return new(exists, exists ? $"Connected to {_endpoint.Host}; {_base} is reachable." : $"Connected, but {_base} does not exist.", _observedHostKey);
        }
        catch (SshConnectionException ex) when (_observedHostKey is not null)
        {
            return new(false, $"Host key not trusted. Verify this fingerprint with the partner, then save it on the endpoint: SHA256:{_observedHostKey}. ({ex.Message})", _observedHostKey);
        }
        catch (Exception ex) { return new(false, ex.Message, _observedHostKey); }
    }

    public ValueTask DisposeAsync()
    {
        if (_client.IsConnected) _client.Disconnect();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
