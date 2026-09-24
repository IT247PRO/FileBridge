using System.ComponentModel.DataAnnotations;
using FileBridge.Core;

namespace FileBridge.Admin.Models;

public sealed class FolderMapModel
{
    public int Id { get; set; }
    [Required] public string SourcePath { get; set; } = "";
    [Required] public string DestinationPath { get; set; } = "";
    public bool Recursive { get; set; }
    public bool PreserveSubfolders { get; set; }
}

public sealed class FilterModel
{
    public int Id { get; set; }
    [Required] public string Pattern { get; set; } = "*";
    public bool IsRegex { get; set; }
    public bool IsExclude { get; set; }
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public int? MinAgeSeconds { get; set; }
    public string? AllowedFileTypes { get; set; }
}

public sealed class SemaphoreModel
{
    public SemaphoreMode SemaphoreModeId { get; set; } = SemaphoreMode.None;
    public string TriggerPattern { get; set; } = "{filename}.done";
    public bool DeleteTriggerAfter { get; set; } = true;
    public bool TransferTrigger { get; set; }
}

public sealed class PostActionModel
{
    public PostActionType PostActionTypeId { get; set; } = PostActionType.Archive;
    public string? ArchivePath { get; set; } = "archive";
    public string? RenamePattern { get; set; }
}

public sealed class NotificationModel
{
    public int Id { get; set; }
    public NotificationEvent NotificationEventId { get; set; }
    public NotificationChannel NotificationChannelId { get; set; }
    [Required] public string Target { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}

public sealed class SlaModel
{
    public int Id { get; set; }
    public TimeOnly ExpectedByLocalTime { get; set; } = new(8, 0);
    public List<DayOfWeek> ActiveDays { get; set; } = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    public string? FilePattern { get; set; }
    public int MinFileCount { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
}

public sealed class JobEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    public string? Description { get; set; }
    public TransferDirection TransferDirectionId { get; set; } = TransferDirection.Inbound;
    [Required] public int SourceEndpointId { get; set; }
    [Required] public int DestinationEndpointId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsPaused { get; set; }

    public ScheduleType ScheduleTypeId { get; set; } = ScheduleType.Interval;
    public string? CronExpression { get; set; }
    public int? IntervalSeconds { get; set; } = 300;
    [Required] public string TimeZoneId { get; set; } = "Central Standard Time";
    public TimeOnly? ActiveFromTime { get; set; }
    public TimeOnly? ActiveToTime { get; set; }
    public List<DayOfWeek> ActiveDays { get; set; } = Enum.GetValues<DayOfWeek>().ToList();

    public int StabilitySeconds { get; set; } = 30;
    [Range(1, 64)] public int MaxParallelFiles { get; set; } = 4;
    [Range(0, 20)] public int MaxRetries { get; set; } = 3;
    public int RetryBaseSeconds { get; set; } = 30;
    public DuplicatePolicy DuplicatePolicyId { get; set; } = DuplicatePolicy.Version;
    public int? EncryptionProfileId { get; set; }
    public CompressionOperation CompressionOperationId { get; set; } = CompressionOperation.None;
    public string? RenamePattern { get; set; }
    public bool UseTempNameOnUpload { get; set; } = true;
    public bool VerifyAfterUpload { get; set; } = true;
    public bool VirusScanEnabled { get; set; }
    public bool GenerateChecksumManifest { get; set; }
    public bool ValidateChecksumManifest { get; set; }
    public bool UseFileWatcher { get; set; }
    public int Version { get; set; }
    public byte[]? RowVersion { get; set; }

    public List<FolderMapModel> FolderMaps { get; set; } = [new()];
    public List<FilterModel> Filters { get; set; } = [];
    public SemaphoreModel Semaphore { get; set; } = new();
    public PostActionModel PostAction { get; set; } = new();
    public List<NotificationModel> Notifications { get; set; } = [];
    public List<SlaModel> SlaRules { get; set; } = [];
}

public sealed class EndpointEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    public EndpointType EndpointTypeId { get; set; } = EndpointType.Smb;
    public string? Host { get; set; }
    public int? Port { get; set; }
    [Required] public string BasePath { get; set; } = "";
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

    // Credential (write-only: never populated from a saved value; blank on Edit means "keep existing")
    public string? CredentialName { get; set; }
    public string? Domain { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? PrivateKey { get; set; }
    public string? Passphrase { get; set; }
    public bool HasStoredCredential { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class EncryptionProfileEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    public EncryptionOperation EncryptionOperationId { get; set; } = EncryptionOperation.None;
    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }
    public string? Passphrase { get; set; }
    public string? OutputExtension { get; set; }
    public bool StripExtensionOnDecrypt { get; set; } = true;
    public bool ArmorOutput { get; set; }
    /// <summary>Set by Save when an AES key was freshly generated; shown once via TempData, never stored in ViewModel state.</summary>
    public string? GeneratedAesKey { get; set; }
    public bool HasStoredKeys { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class DashboardViewModel
{
    public int SucceededToday { get; set; }
    public int FailedToday { get; set; }
    public int InProgress { get; set; }
    public long BytesToday { get; set; }
    public int HeldQuarantine { get; set; }
    public int PendingApprovals { get; set; }
    public bool KillSwitchOn { get; set; }
    public List<(string NodeName, string NodeRole, DateTime LastSeenUtc)> Nodes { get; set; } = [];
    public List<(string JobName, int Failed, DateTime LastRunUtc)> RecentFailingJobs { get; set; } = [];
}
