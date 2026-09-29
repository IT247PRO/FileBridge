using FileBridge.Core;
using FileBridge.Core.Rules;

namespace FileBridge.Admin;

/// <summary>Small display helpers shared by the views.</summary>
public static class Ui
{
    // Every page displays timestamps in Central time -- this deployment's operating timezone and the same
    // zone job schedules already run in (see ScheduleSyncJob/ProcessScheduleSyncJob's `.InTimeZone(tz)`).
    // Storage and every scheduling/comparison/retention/heartbeat check stay UTC everywhere else in the app:
    // Central time repeats an hour every fall DST changeover, which would silently corrupt anything that
    // stored or compared it directly. This is purely a display-layer conversion, applied at the last moment.
    // (Previously this used DateTime.ToLocalTime(), i.e. whatever OS timezone the Admin server happened to be
    // set to, with no zone label -- silently wrong on a server not configured for Central time.)
    private static readonly TimeZoneInfo Central = ScheduleWindow.FindZone("Central Standard Time");

    private static DateTime ToCentral(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc), Central);

    private static string Zone(DateTime central) => Central.IsDaylightSavingTime(central) ? "CDT" : "CST";

    /// <summary>Inverse of ToCentral(): converts a Central-time wall-clock value (e.g. from a &lt;input
    /// type="datetime-local"&gt; the admin filled in, which carries no offset of its own) to UTC for storage.</summary>
    public static DateTime CentralToUtc(DateTime central) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(central, DateTimeKind.Unspecified), Central);

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

    public static string Local(DateTime? utc)
    {
        if (utc is not { } v) return "-";
        var local = ToCentral(v);
        return $"{local:MMM d, h:mm:ss tt} {Zone(local)}";
    }

    /// <summary>Same conversion as Local(), but in a fixed-width sortable format for monospace table columns
    /// (replaces the old DateTime.ToString("u") -- which showed raw UTC with a literal "Z" suffix).</summary>
    public static string LocalSortable(DateTime? utc)
    {
        if (utc is not { } v) return "-";
        var local = ToCentral(v);
        return $"{local:yyyy-MM-dd HH:mm:ss} {Zone(local)}";
    }

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
