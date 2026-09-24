namespace FileBridge.Infrastructure;

public sealed class EngineOptions
{
    /// <summary>Local, per-node scratch folder. Fast disk, excluded from real-time AV (scans are explicit).</summary>
    public string StagingRoot { get; set; } = @"D:\FileBridge\Staging";
    /// <summary>Shared UNC path so any node can release a quarantined file.</summary>
    public string QuarantineRoot { get; set; } = @"\\fileserver\FileBridge$\Quarantine";
    public int LeaseMinutes { get; set; } = 60;
    public int FailedCooldownMinutes { get; set; } = 15;
    public int MaxLeaseAttempts { get; set; } = 5;
    public string DefenderPath { get; set; } = @"C:\Program Files\Windows Defender\MpCmdRun.exe";
    public long MaxUnzipBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public int MaxUnzipEntries { get; set; } = 10_000;
}

public sealed class NotificationOptions
{
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 25;
    public bool UseStartTls { get; set; } = true;
    public string From { get; set; } = "filebridge@agency.gov";
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Public URL of the admin site (load balancer VIP), used for links in alerts.</summary>
    public string AdminUrl { get; set; } = "https://filebridge.agency.gov";
}
