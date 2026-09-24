namespace FileBridge.Core.Entities;

/// <summary>Base for reference tables (lkp*). Rows are seeded from Enums.cs.</summary>
public abstract class LookupEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Configuration rows edited from the admin UI. Stamped and audited automatically.</summary>
public abstract class AuditableEntity
{
    public DateTime CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? ModifiedUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class Credential : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Domain { get; set; }
    public string? Username { get; set; }
    public string? ProtectedPassword { get; set; }
    public string? ProtectedPrivateKey { get; set; }
    public string? ProtectedPassphrase { get; set; }
}

public sealed class Endpoint : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public EndpointType EndpointTypeId { get; set; } = EndpointType.Smb;
    public string? Host { get; set; }
    public int? Port { get; set; }
    /// <summary>UNC root for SMB (\\server\share\root), remote root for SFTP (/outbound), path prefix for HTTPS.</summary>
    public string BasePath { get; set; } = "";
    public int? CredentialId { get; set; }
    public Credential? Credential { get; set; }
    /// <summary>SFTP host key pin, SHA256 base64 (as shown by ssh-keygen -lf).</summary>
    public string? HostKeyFingerprint { get; set; }
    public string? BaseUrl { get; set; }
    public string? ListRoute { get; set; }
    public string? DownloadRoute { get; set; }
    public string? UploadRoute { get; set; }
    public string? DeleteRoute { get; set; }
    public string? RenameRoute { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public bool IsEnabled { get; set; } = true;
    public string? Notes { get; set; }
}

public sealed class EncryptionProfile : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public EncryptionOperation EncryptionOperationId { get; set; } = EncryptionOperation.None;
    public string? ProtectedPublicKey { get; set; }
    public string? ProtectedPrivateKey { get; set; }
    public string? ProtectedPassphrase { get; set; }
    public string? ProtectedAesKey { get; set; }
    public string? OutputExtension { get; set; }
    public bool StripExtensionOnDecrypt { get; set; } = true;
    public bool ArmorOutput { get; set; }
}

public sealed class Job : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public TransferDirection TransferDirectionId { get; set; } = TransferDirection.Inbound;
    public int SourceEndpointId { get; set; }
    public Endpoint? SourceEndpoint { get; set; }
    public int DestinationEndpointId { get; set; }
    public Endpoint? DestinationEndpoint { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsPaused { get; set; }

    public ScheduleType ScheduleTypeId { get; set; } = ScheduleType.Interval;
    /// <summary>Quartz cron (with seconds), e.g. "0 0/5 * * * ?".</summary>
    public string? CronExpression { get; set; }
    public int? IntervalSeconds { get; set; } = 300;
    public string TimeZoneId { get; set; } = "Central Standard Time";
    public TimeOnly? ActiveFromTime { get; set; }
    public TimeOnly? ActiveToTime { get; set; }
    /// <summary>Bit per DayOfWeek (Sunday = bit 0). 127 = every day.</summary>
    public int ActiveDaysMask { get; set; } = 127;

    public int StabilitySeconds { get; set; } = 30;
    public int MaxParallelFiles { get; set; } = 4;
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseSeconds { get; set; } = 30;
    public DuplicatePolicy DuplicatePolicyId { get; set; } = DuplicatePolicy.Version;
    public int? EncryptionProfileId { get; set; }
    public EncryptionProfile? EncryptionProfile { get; set; }
    public CompressionOperation CompressionOperationId { get; set; } = CompressionOperation.None;
    public string? RenamePattern { get; set; }
    public bool UseTempNameOnUpload { get; set; } = true;
    public bool VerifyAfterUpload { get; set; } = true;
    public bool VirusScanEnabled { get; set; }
    public bool GenerateChecksumManifest { get; set; }
    public bool ValidateChecksumManifest { get; set; }
    public bool UseFileWatcher { get; set; }
    public int Version { get; set; }

    public List<JobFolderMap> FolderMaps { get; set; } = new();
    public List<FileFilter> Filters { get; set; } = new();
    public SemaphoreRule? SemaphoreRule { get; set; }
    public PostAction? PostAction { get; set; }
    public List<NotificationRule> NotificationRules { get; set; } = new();
    public List<SlaRule> SlaRules { get; set; } = new();
    public List<BlackoutWindow> BlackoutWindows { get; set; } = new();
}

public sealed class JobFolderMap
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public bool Recursive { get; set; }
    public bool PreserveSubfolders { get; set; }
}

public sealed class FileFilter
{
    public int Id { get; set; }
    public int JobId { get; set; }
    /// <summary>Glob (*.csv, **/in/*.txt) or regex when IsRegex.</summary>
    public string Pattern { get; set; } = "*";
    public bool IsRegex { get; set; }
    public bool IsExclude { get; set; }
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public int? MinAgeSeconds { get; set; }
    /// <summary>Comma list checked by content sniffing: pdf,zip,csv,text,xml,json,pgp,png,jpeg,gif,tiff,gzip.</summary>
    public string? AllowedFileTypes { get; set; }
}

public sealed class SemaphoreRule
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public SemaphoreMode SemaphoreModeId { get; set; } = SemaphoreMode.None;
    /// <summary>PerFile: "{filename}.done" / "{name}.ok". Batch: "READY.flg".</summary>
    public string TriggerPattern { get; set; } = "{filename}.done";
    public bool DeleteTriggerAfter { get; set; } = true;
    public bool TransferTrigger { get; set; }
}

public sealed class PostAction
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public PostActionType PostActionTypeId { get; set; } = PostActionType.Archive;
    public string? ArchivePath { get; set; } = "archive";
    public string? RenamePattern { get; set; }
}

public sealed class NotificationRule
{
    public int Id { get; set; }
    /// <summary>Null = applies to every job.</summary>
    public int? JobId { get; set; }
    public NotificationEvent NotificationEventId { get; set; }
    public NotificationChannel NotificationChannelId { get; set; }
    /// <summary>Email: semicolon list. Teams: protected webhook URL.</summary>
    public string Target { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}

public sealed class SlaRule
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public TimeOnly ExpectedByLocalTime { get; set; } = new(8, 0);
    public int DaysMask { get; set; } = 62; // Mon-Fri
    public string? FilePattern { get; set; }
    public int MinFileCount { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public DateOnly? LastBreachDate { get; set; }
}

public sealed class BlackoutWindow
{
    public int Id { get; set; }
    /// <summary>Null = global blackout (all jobs).</summary>
    public int? JobId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? Reason { get; set; }
}

public sealed class GlobalSetting : AuditableEntity
{
    public string SettingKey { get; set; } = "";
    public string? SettingValue { get; set; }
    public string? Description { get; set; }
    public bool IsSecret { get; set; }
}

public sealed class RoleMapping : AuditableEntity
{
    public int Id { get; set; }
    /// <summary>DOMAIN\Group, DOMAIN\user, or a raw SID.</summary>
    public string AdGroup { get; set; } = "";
    public AppRole AppRoleId { get; set; } = AppRole.Viewer;
}
