namespace FileBridge.Core.Rules;

/// <summary>Endpoint-relative paths always use '/' and never start or end with one.</summary>
public static class RemotePath
{
    public static string Normalize(string? path) =>
        (path ?? "").Replace('\\', '/').Trim('/').Trim();

    public static string Combine(params string?[] parts) =>
        string.Join('/', parts.Select(Normalize).Where(p => p.Length > 0));

    public static string GetDirectory(string path)
    {
        var p = Normalize(path);
        var i = p.LastIndexOf('/');
        return i < 0 ? "" : p[..i];
    }

    public static string GetFileName(string path)
    {
        var p = Normalize(path);
        var i = p.LastIndexOf('/');
        return i < 0 ? p : p[(i + 1)..];
    }

    public static string Sanitize(string fileName)
    {
        var invalid = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0' };
        var chars = fileName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim().TrimEnd('.');
        return s.Length == 0 ? "_" : s;
    }
}
