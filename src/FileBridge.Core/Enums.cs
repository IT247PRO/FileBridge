namespace FileBridge.Core;

public enum EndpointType
{
    Smb = 1,
    Sftp = 2,
    Https = 3,
    LocalDisk = 4
}

public enum TransferStatus
{
    Pending = 1,
    InProgress = 2,
    Success = 3,
    Failed = 4,
    Quarantined = 5,
    Skipped = 6
}

public enum JobTriggerType
{
    Scheduled = 1,
    FileWatcher = 2,
    Manual = 3,
    Api = 4
}

public enum QuarantineReason
{
    VirusDetected = 1,
    ChecksumMismatch = 2,
    InvalidFileType = 3,
    SizeLimitExceeded = 4,
    PolicyViolation = 5
}

public enum QuarantineStatus
{
    Held = 1,
    Released = 2,
    Discarded = 3
}

public enum ApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public enum RoleType
{
    Viewer = 1,
    Operator = 2,
    Admin = 3,
    Approver = 4
}
