namespace FileBridge.Core;

// Each enum maps 1:1 to a lookup table named lkp<EnumName> (see db/01_lookups.sql),
// and to a foreign-key column named <EnumName>Id on the tbl* tables.
public enum EndpointType { Smb = 1, Sftp = 2, Https = 3, LocalDisk = 4 }
public enum TransferDirection { Inbound = 1, Outbound = 2 }
public enum TransferStatus { Pending = 1, InProgress = 2, Succeeded = 3, Failed = 4, Skipped = 5, Quarantined = 6, Duplicate = 7, DryRun = 8 }
public enum PostActionType { None = 1, Delete = 2, Archive = 3, Rename = 4 }
public enum SemaphoreMode { None = 1, PerFile = 2, Batch = 3 }
public enum EncryptionOperation { None = 1, PgpEncrypt = 2, PgpDecrypt = 3, PgpEncryptAndSign = 4, PgpDecryptAndVerify = 5, AesEncrypt = 6, AesDecrypt = 7 }
public enum CompressionOperation { None = 1, Zip = 2, Unzip = 3 }
public enum DuplicatePolicy { Skip = 1, Overwrite = 2, Version = 3, Fail = 4 }
public enum ScheduleType { Cron = 1, Interval = 2 }
public enum NotificationChannel { Email = 1, Teams = 2 }
public enum NotificationEvent { JobFailed = 1, FileQuarantined = 2, SlaBreach = 3, JobSucceeded = 4, ApprovalRequested = 5 }
public enum ApprovalStatus { Pending = 1, Approved = 2, Rejected = 3 }
public enum RequestType { RunNow = 1, DryRun = 2, TestConnection = 3, Browse = 4, ReleaseQuarantine = 5, DiscardQuarantine = 6 }
public enum RequestStatus { Queued = 1, Running = 2, Completed = 3, Failed = 4 }
public enum QuarantineStatus { Held = 1, Released = 2, Discarded = 3 }
public enum AppRole { Viewer = 1, Operator = 2, Admin = 3, Approver = 4 }
public enum AuditAction { Added = 1, Modified = 2, Deleted = 3 }
