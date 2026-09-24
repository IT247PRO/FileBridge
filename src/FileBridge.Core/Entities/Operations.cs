namespace FileBridge.Core.Entities;

/// <summary>High-volume runtime rows. Not stamped, not written to tblConfigAudit.</summary>
public interface IOperationalEntity { }

public sealed class TransferHistory : IOperationalEntity
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public Guid RunId { get; set; }
    public string CorrelationId { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string? DestinationPath { get; set; }
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public TransferStatus TransferStatusId { get; set; } = TransferStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string NodeName { get; set; } = "";
    public string TriggeredBy { get; set; } = "";
}

/// <summary>Idempotency + cluster claim. Unique on (JobId, Fingerprint).</summary>
public sealed class FileLease : IOperationalEntity
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string LeasedBy { get; set; } = "";
    public DateTime LeaseExpiresUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public TransferStatus TransferStatusId { get; set; } = TransferStatus.InProgress;
    public int AttemptCount { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
}

public sealed class Quarantine : IOperationalEntity
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public int? FolderMapId { get; set; }
    public long? TransferHistoryId { get; set; }
    public string OriginalPath { get; set; } = "";
    public string QuarantinePath { get; set; } = "";
    public string Reason { get; set; } = "";
    public QuarantineStatus QuarantineStatusId { get; set; } = QuarantineStatus.Held;
    public DateTime CreatedUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedUtc { get; set; }
}

/// <summary>Admin -> Worker command queue. The web tier never touches file systems itself.</summary>
public sealed class RunRequest : IOperationalEntity
{
    public long Id { get; set; }
    public RequestType RequestTypeId { get; set; }
    public int? JobId { get; set; }
    public int? EndpointId { get; set; }
    public int? QuarantineId { get; set; }
    public string? Path { get; set; }
    public RequestStatus RequestStatusId { get; set; } = RequestStatus.Queued;
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedUtc { get; set; }
    public string? PickedBy { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? ResultJson { get; set; }
}

public sealed class NodeHeartbeat : IOperationalEntity
{
    public string NodeName { get; set; } = "";
    public string NodeRole { get; set; } = "";
    public DateTime LastSeenUtc { get; set; }
    public DateTime StartedUtc { get; set; }
    public string? AppVersion { get; set; }
}

public sealed class JobVersion : IOperationalEntity
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public int Version { get; set; }
    public string SnapshotJson { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public sealed class ChangeRequest : IOperationalEntity
{
    public long Id { get; set; }
    public string EntityName { get; set; } = "";
    public string? EntityKey { get; set; }
    public string PayloadJson { get; set; } = "";
    public ApprovalStatus ApprovalStatusId { get; set; } = ApprovalStatus.Pending;
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public string? ReviewComment { get; set; }
}

public sealed class ConfigAudit : IOperationalEntity
{
    public long Id { get; set; }
    public string EntityName { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public AuditAction AuditActionId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string ChangedBy { get; set; } = "";
    public DateTime ChangedUtc { get; set; }
    public string Host { get; set; } = "";
}
