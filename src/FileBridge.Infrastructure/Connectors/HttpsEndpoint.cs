using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FileBridge.Core;
using FileBridge.Core.Entities;
using FileBridge.Core.Rules;

namespace FileBridge.Infrastructure.Connectors;

/// <summary>
/// Generic REST file API. Routes are configurable per endpoint; each receives ?path= (endpoint-relative + BasePath).
///   List:     GET  {ListRoute}?path=&amp;recursive=  -> [{ "path", "name", "size", "lastModifiedUtc" }]  (add &amp;folders=true for folder names)
///   Download: GET  {DownloadRoute}?path=        -> file bytes (HEAD returns Content-Length)
///   Upload:   PUT  {UploadRoute}?path=&amp;overwrite= -> 2xx
///   Rename:   POST {RenameRoute}?from=&amp;to=&amp;overwrite=
///   Delete:   DELETE {DeleteRoute}?path=
/// Auth: Basic when a username is set, otherwise the secret is sent as a Bearer token.
/// For any partner system whose real API differs, subclass this or add a dedicated connector behind IFileEndpoint.
/// </summary>
public sealed class HttpsEndpoint(Endpoint endpoint, HttpClient http, string? user, string? secret) : IFileEndpoint
{
    private sealed record Item(string Path, string Name, long Size, DateTimeOffset LastModifiedUtc);

    private string Remote(string rel) => RemotePath.Combine(endpoint.BasePath, rel);

    private Uri Url(string? route, params (string Key, string Value)[] query)
    {
        var baseUrl = (endpoint.BaseUrl ?? throw new NonRetryableException("HTTPS BaseUrl is required.")).TrimEnd('/');
        var r = (route ?? "").TrimStart('/');
        var qs = string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));
        var sep = r.Contains('?') ? "&" : "?";
        return new Uri(qs.Length == 0 ? $"{baseUrl}/{r}" : $"{baseUrl}/{r}{sep}{qs}");
    }

    private HttpRequestMessage Request(HttpMethod method, Uri url)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(secret))
            req.Headers.Authorization = string.IsNullOrEmpty(user)
                ? new AuthenticationHeaderValue("Bearer", secret)
                : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}")));
        return req;
    }

    private CancellationTokenSource Short(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(endpoint.TimeoutSeconds));
        return cts;
    }

    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<IReadOnlyList<RemoteFile>> ListAsync(string folder, bool recursive, CancellationToken ct)
    {
        using var cts = Short(ct);
        using var resp = await http.SendAsync(Request(HttpMethod.Get, Url(endpoint.ListRoute, ("path", Remote(folder)), ("recursive", recursive ? "true" : "false"))), cts.Token);
        resp.EnsureSuccessStatusCode();
        var items = await resp.Content.ReadFromJsonAsync<List<Item>>(cancellationToken: cts.Token) ?? new();
        var prefix = RemotePath.Normalize(endpoint.BasePath);
        return items.Select(i =>
        {
            var p = RemotePath.Normalize(i.Path);
            if (prefix.Length > 0 && p.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) p = p[(prefix.Length + 1)..];
            return new RemoteFile(p, i.Name, i.Size, i.LastModifiedUtc);
        }).ToList();
    }

    public async Task<IReadOnlyList<string>> ListFoldersAsync(string folder, CancellationToken ct)
    {
        using var cts = Short(ct);
        using var resp = await http.SendAsync(Request(HttpMethod.Get, Url(endpoint.ListRoute, ("path", Remote(folder)), ("folders", "true"))), cts.Token);
        if (!resp.IsSuccessStatusCode) return Array.Empty<string>();
        var names = await resp.Content.ReadFromJsonAsync<List<string>>(cancellationToken: cts.Token) ?? new();
        return names.Select(n => RemotePath.Combine(folder, RemotePath.GetFileName(n))).ToList();
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct) => await GetSizeAsync(path, ct) is not null;

    public async Task<long?> GetSizeAsync(string path, CancellationToken ct)
    {
        using var cts = Short(ct);
        using var resp = await http.SendAsync(Request(HttpMethod.Head, Url(endpoint.DownloadRoute, ("path", Remote(path)))), cts.Token);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return resp.Content.Headers.ContentLength;
    }

    public Task<bool> IsLockedAsync(string path, CancellationToken ct) => Task.FromResult(false);

    public async Task DownloadAsync(string path, Stream destination, CancellationToken ct)
    {
        using var resp = await http.SendAsync(Request(HttpMethod.Get, Url(endpoint.DownloadRoute, ("path", Remote(path)))), HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var body = await resp.Content.ReadAsStreamAsync(ct);
        await body.CopyToAsync(destination, ct);
    }

    public async Task UploadAsync(Stream source, string path, bool overwrite, CancellationToken ct)
    {
        var req = Request(HttpMethod.Put, Url(endpoint.UploadRoute, ("path", Remote(path)), ("overwrite", overwrite ? "true" : "false")));
        req.Content = new StreamContent(source);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct)
    {
        using var cts = Short(ct);
        using var resp = await http.SendAsync(Request(HttpMethod.Post, Url(endpoint.RenameRoute, ("from", Remote(from)), ("to", Remote(to)), ("overwrite", overwrite ? "true" : "false"))), cts.Token);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var cts = Short(ct);
        using var resp = await http.SendAsync(Request(HttpMethod.Delete, Url(endpoint.DeleteRoute, ("path", Remote(path)))), cts.Token);
        if (resp.StatusCode != HttpStatusCode.NotFound) resp.EnsureSuccessStatusCode();
    }

    public async Task<ConnectionTestResult> TestAsync(CancellationToken ct)
    {
        try
        {
            var files = await ListAsync("", false, ct);
            return new(true, $"Connected to {endpoint.BaseUrl}; {files.Count} file(s) visible at the root.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
