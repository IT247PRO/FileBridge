namespace FileBridge.Core;

public class Endpoint
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EndpointType EndpointTypeId { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? BasePath { get; set; }
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? EncryptedPrivateKey { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Job
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SourceEndpointId { get; set; }
    public Endpoint? SourceEndpoint { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string FilePattern { get; set; } = "*.*";
    public int DestinationEndpointId { get; set; }
    public Endpoint? DestinationEndpoint { get; set; }
    public string DestinationPath { get; set; } = string.Empty;
    public string CronExpression { get; set; } = "0 0 * * * ?";
    public JobTriggerType TriggerTypeId { get; set; } = JobTriggerType.Scheduled;
    public bool OverwriteDestination { get; set; } = false;
    public bool DeleteSourceAfterTransfer { get; set; } = false;
    public int? EncryptionProfileId { get; set; }
    public bool RequireApproval { get; set; } = false;
    public int MaxRetries { get; set; } = 3;
    public int RetryBackoffSeconds { get; set; } = 60;
    public DateTime? LastRunUtc { get; set; }
    public TransferStatus? LastRunStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class TransferHistory
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("D");
    public TransferStatus StatusId { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int FilesTransferred { get; set; }
    public int FilesFailed { get; set; }
    public long BytesTransferred { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExecutionNode { get; set; }
}

public class QuarantineItem
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string QuarantinedFilePath { get; set; } = string.Empty;
    public QuarantineReason ReasonId { get; set; }
    public QuarantineStatus StatusId { get; set; } = QuarantineStatus.Held;
    public string? Sha256 { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime QuarantinedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}

public class RunRequest
{
    public long Id { get; set; }
    public string RequestType { get; set; } = string.Empty; // TestEndpoint, BrowseEndpoint, RunJob, ReleaseQuarantine
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Pending"; // Pending, Claimed, Completed, Failed
    public string? ClaimedByNode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? ResultJson { get; set; }
}

public class GlobalSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class RoleMapping
{
    public int Id { get; set; }
    public string PrincipalName { get; set; } = string.Empty; // User or Group
    public RoleType RoleTypeId { get; set; }
}
