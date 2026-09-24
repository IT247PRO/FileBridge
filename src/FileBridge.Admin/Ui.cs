using FileBridge.Core;

namespace FileBridge.Admin;

/// <summary>Small display helpers shared by the views.</summary>
public static class Ui
{
    public static string StatusClass(TransferStatus s) => s switch
    {
        TransferStatus.Succeeded => "st-ok",
        TransferStatus.Failed => "st-fail",
        TransferStatus.Quarantined => "st-quar",
        TransferStatus.InProgress => "st-run",
        TransferStatus.Skipped or TransferStatus.Duplicate => "st-muted",
        _ => "st-muted"
    };

    public static string StatusLabel(TransferStatus s) => s switch
    {
        TransferStatus.Succeeded => "Delivered",
        TransferStatus.InProgress => "Moving",
        TransferStatus.Quarantined => "Held",
        _ => s.ToString()
    };

    public static string Bytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b; var i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{b} B" : $"{v:0.#} {u[i]}";
    }

    public static string Local(DateTime? utc) => utc is null ? "" : utc.Value.ToLocalTime().ToString("MMM d, h:mm:ss tt");

    public static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalSeconds < 60 ? $"{(int)d.TotalSeconds}s ago" : d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes}m ago" : d.TotalHours < 48 ? $"{(int)d.TotalHours}h ago" : $"{(int)d.TotalDays}d ago";
    }

    public static string EndpointGlyph(EndpointType t) => t switch
    {
        EndpointType.Smb => "SMB", EndpointType.Sftp => "SFTP", EndpointType.Https => "HTTPS", _ => "DISK"
    };

    public static string Words(string pascal) => System.Text.RegularExpressions.Regex.Replace(pascal, "(?<=[a-z])(?=[A-Z])", " ");
}
