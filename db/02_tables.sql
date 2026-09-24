-- =============================================================
-- FileBridge transactional / configuration tables (tbl*).
-- Requires 01_lookups.sql. Matches FileBridgeDbContext (EF maps: lookups -> lkp<Name>, all else -> tbl<Name>).
-- Enum columns are named <Enum>Id and reference lkp<Enum>.
-- =============================================================
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ---------- Configuration (audited, RowVersion for optimistic concurrency across load-balanced admins) ----------

CREATE TABLE dbo.tblCredential (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblCredential PRIMARY KEY,
    Name                 NVARCHAR(100)  NOT NULL CONSTRAINT UQ_tblCredential_Name UNIQUE,
    Domain               NVARCHAR(100)  NULL,
    Username             NVARCHAR(200)  NULL,
    ProtectedPassword    NVARCHAR(MAX)  NULL,   -- Data Protection ciphertext, never plaintext
    ProtectedPrivateKey  NVARCHAR(MAX)  NULL,
    ProtectedPassphrase  NVARCHAR(MAX)  NULL,
    CreatedUtc           DATETIME2(3)   NOT NULL,
    CreatedBy            NVARCHAR(256)  NOT NULL,
    ModifiedUtc          DATETIME2(3)   NULL,
    ModifiedBy           NVARCHAR(256)  NULL,
    RowVersion           ROWVERSION     NOT NULL
);
GO

CREATE TABLE dbo.tblEndpoint (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblEndpoint PRIMARY KEY,
    Name                 NVARCHAR(100)  NOT NULL CONSTRAINT UQ_tblEndpoint_Name UNIQUE,
    EndpointTypeId       INT            NOT NULL CONSTRAINT FK_tblEndpoint_lkpEndpointType REFERENCES dbo.lkpEndpointType (Id),
    Host                 NVARCHAR(255)  NULL,
    Port                 INT            NULL,
    BasePath             NVARCHAR(500)  NOT NULL CONSTRAINT DF_tblEndpoint_BasePath DEFAULT (N''),
    CredentialId         INT            NULL CONSTRAINT FK_tblEndpoint_tblCredential REFERENCES dbo.tblCredential (Id),
    HostKeyFingerprint   NVARCHAR(200)  NULL,
    BaseUrl              NVARCHAR(500)  NULL,
    ListRoute            NVARCHAR(300)  NULL,
    DownloadRoute        NVARCHAR(300)  NULL,
    UploadRoute          NVARCHAR(300)  NULL,
    DeleteRoute          NVARCHAR(300)  NULL,
    RenameRoute          NVARCHAR(300)  NULL,
    TimeoutSeconds       INT            NOT NULL CONSTRAINT DF_tblEndpoint_TimeoutSeconds DEFAULT (60),
    IsEnabled            BIT            NOT NULL CONSTRAINT DF_tblEndpoint_IsEnabled DEFAULT (1),
    Notes                NVARCHAR(1000) NULL,
    CreatedUtc           DATETIME2(3)   NOT NULL,
    CreatedBy            NVARCHAR(256)  NOT NULL,
    ModifiedUtc          DATETIME2(3)   NULL,
    ModifiedBy           NVARCHAR(256)  NULL,
    RowVersion           ROWVERSION     NOT NULL
);
GO

CREATE TABLE dbo.tblEncryptionProfile (
    Id                      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblEncryptionProfile PRIMARY KEY,
    Name                    NVARCHAR(100) NOT NULL CONSTRAINT UQ_tblEncryptionProfile_Name UNIQUE,
    EncryptionOperationId   INT           NOT NULL CONSTRAINT FK_tblEncryptionProfile_lkpEncryptionOperation REFERENCES dbo.lkpEncryptionOperation (Id),
    ProtectedPublicKey      NVARCHAR(MAX) NULL,
    ProtectedPrivateKey     NVARCHAR(MAX) NULL,
    ProtectedPassphrase     NVARCHAR(MAX) NULL,
    ProtectedAesKey         NVARCHAR(MAX) NULL,
    OutputExtension         NVARCHAR(20)  NULL,
    StripExtensionOnDecrypt BIT           NOT NULL CONSTRAINT DF_tblEncryptionProfile_Strip DEFAULT (1),
    ArmorOutput             BIT           NOT NULL CONSTRAINT DF_tblEncryptionProfile_Armor DEFAULT (0),
    CreatedUtc              DATETIME2(3)  NOT NULL,
    CreatedBy               NVARCHAR(256) NOT NULL,
    ModifiedUtc             DATETIME2(3)  NULL,
    ModifiedBy              NVARCHAR(256) NULL,
    RowVersion              ROWVERSION    NOT NULL
);
GO

CREATE TABLE dbo.tblJob (
    Id                        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblJob PRIMARY KEY,
    Name                      NVARCHAR(100)  NOT NULL CONSTRAINT UQ_tblJob_Name UNIQUE,
    Description               NVARCHAR(1000) NULL,
    TransferDirectionId       INT            NOT NULL CONSTRAINT FK_tblJob_lkpTransferDirection REFERENCES dbo.lkpTransferDirection (Id),
    SourceEndpointId          INT            NOT NULL CONSTRAINT FK_tblJob_tblEndpoint_Source REFERENCES dbo.tblEndpoint (Id),
    DestinationEndpointId     INT            NOT NULL CONSTRAINT FK_tblJob_tblEndpoint_Destination REFERENCES dbo.tblEndpoint (Id),
    IsEnabled                 BIT            NOT NULL CONSTRAINT DF_tblJob_IsEnabled DEFAULT (1),
    IsPaused                  BIT            NOT NULL CONSTRAINT DF_tblJob_IsPaused DEFAULT (0),
    ScheduleTypeId            INT            NOT NULL CONSTRAINT FK_tblJob_lkpScheduleType REFERENCES dbo.lkpScheduleType (Id),
    CronExpression            NVARCHAR(120)  NULL,
    IntervalSeconds           INT            NULL CONSTRAINT CK_tblJob_IntervalSeconds CHECK (IntervalSeconds IS NULL OR IntervalSeconds >= 15),
    TimeZoneId                NVARCHAR(100)  NOT NULL CONSTRAINT DF_tblJob_TimeZoneId DEFAULT (N'Central Standard Time'),
    ActiveFromTime            TIME(0)        NULL,
    ActiveToTime              TIME(0)        NULL,
    ActiveDaysMask            INT            NOT NULL CONSTRAINT DF_tblJob_ActiveDaysMask DEFAULT (127),
    StabilitySeconds          INT            NOT NULL CONSTRAINT DF_tblJob_StabilitySeconds DEFAULT (30),
    MaxParallelFiles          INT            NOT NULL CONSTRAINT DF_tblJob_MaxParallelFiles DEFAULT (4) CONSTRAINT CK_tblJob_MaxParallelFiles CHECK (MaxParallelFiles BETWEEN 1 AND 16),
    MaxRetries                INT            NOT NULL CONSTRAINT DF_tblJob_MaxRetries DEFAULT (3),
    RetryBaseSeconds          INT            NOT NULL CONSTRAINT DF_tblJob_RetryBaseSeconds DEFAULT (30),
    DuplicatePolicyId         INT            NOT NULL CONSTRAINT FK_tblJob_lkpDuplicatePolicy REFERENCES dbo.lkpDuplicatePolicy (Id),
    EncryptionProfileId       INT            NULL CONSTRAINT FK_tblJob_tblEncryptionProfile REFERENCES dbo.tblEncryptionProfile (Id),
    CompressionOperationId    INT            NOT NULL CONSTRAINT FK_tblJob_lkpCompressionOperation REFERENCES dbo.lkpCompressionOperation (Id),
    RenamePattern             NVARCHAR(300)  NULL,
    UseTempNameOnUpload       BIT            NOT NULL CONSTRAINT DF_tblJob_UseTempName DEFAULT (1),
    VerifyAfterUpload         BIT            NOT NULL CONSTRAINT DF_tblJob_Verify DEFAULT (1),
    VirusScanEnabled          BIT            NOT NULL CONSTRAINT DF_tblJob_VirusScan DEFAULT (0),
    GenerateChecksumManifest  BIT            NOT NULL CONSTRAINT DF_tblJob_GenManifest DEFAULT (0),
    ValidateChecksumManifest  BIT            NOT NULL CONSTRAINT DF_tblJob_ValManifest DEFAULT (0),
    UseFileWatcher            BIT            NOT NULL CONSTRAINT DF_tblJob_Watcher DEFAULT (0),
    Version                   INT            NOT NULL CONSTRAINT DF_tblJob_Version DEFAULT (1),
    CreatedUtc                DATETIME2(3)   NOT NULL,
    CreatedBy                 NVARCHAR(256)  NOT NULL,
    ModifiedUtc               DATETIME2(3)   NULL,
    ModifiedBy                NVARCHAR(256)  NULL,
    RowVersion                ROWVERSION     NOT NULL
);
GO

CREATE TABLE dbo.tblJobFolderMap (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblJobFolderMap PRIMARY KEY,
    JobId               INT           NOT NULL CONSTRAINT FK_tblJobFolderMap_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE,
    SourcePath          NVARCHAR(500) NOT NULL,
    DestinationPath     NVARCHAR(500) NOT NULL,
    Recursive           BIT           NOT NULL CONSTRAINT DF_tblJobFolderMap_Recursive DEFAULT (0),
    PreserveSubfolders  BIT           NOT NULL CONSTRAINT DF_tblJobFolderMap_Preserve DEFAULT (0)
);
CREATE INDEX IX_tblJobFolderMap_JobId ON dbo.tblJobFolderMap (JobId);
GO

CREATE TABLE dbo.tblFileFilter (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblFileFilter PRIMARY KEY,
    JobId             INT           NOT NULL CONSTRAINT FK_tblFileFilter_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE,
    Pattern           NVARCHAR(400) NOT NULL,
    IsRegex           BIT           NOT NULL CONSTRAINT DF_tblFileFilter_IsRegex DEFAULT (0),
    IsExclude         BIT           NOT NULL CONSTRAINT DF_tblFileFilter_IsExclude DEFAULT (0),
    MinSizeBytes      BIGINT        NULL,
    MaxSizeBytes      BIGINT        NULL,
    MinAgeSeconds     INT           NULL,
    AllowedFileTypes  NVARCHAR(200) NULL
);
CREATE INDEX IX_tblFileFilter_JobId ON dbo.tblFileFilter (JobId);
GO

CREATE TABLE dbo.tblSemaphoreRule (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblSemaphoreRule PRIMARY KEY,
    JobId               INT           NOT NULL CONSTRAINT FK_tblSemaphoreRule_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE
                                       CONSTRAINT UQ_tblSemaphoreRule_JobId UNIQUE,
    SemaphoreModeId     INT           NOT NULL CONSTRAINT FK_tblSemaphoreRule_lkpSemaphoreMode REFERENCES dbo.lkpSemaphoreMode (Id),
    TriggerPattern      NVARCHAR(300) NOT NULL,
    DeleteTriggerAfter  BIT           NOT NULL CONSTRAINT DF_tblSemaphoreRule_Delete DEFAULT (1),
    TransferTrigger     BIT           NOT NULL CONSTRAINT DF_tblSemaphoreRule_Transfer DEFAULT (0)
);
GO

CREATE TABLE dbo.tblPostAction (
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblPostAction PRIMARY KEY,
    JobId             INT           NOT NULL CONSTRAINT FK_tblPostAction_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE
                                     CONSTRAINT UQ_tblPostAction_JobId UNIQUE,
    PostActionTypeId  INT           NOT NULL CONSTRAINT FK_tblPostAction_lkpPostActionType REFERENCES dbo.lkpPostActionType (Id),
    ArchivePath       NVARCHAR(500) NULL,
    RenamePattern     NVARCHAR(300) NULL
);
GO

CREATE TABLE dbo.tblNotificationRule (
    Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNotificationRule PRIMARY KEY,
    JobId                  INT           NULL CONSTRAINT FK_tblNotificationRule_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE, -- NULL = all jobs
    NotificationEventId    INT           NOT NULL CONSTRAINT FK_tblNotificationRule_lkpNotificationEvent REFERENCES dbo.lkpNotificationEvent (Id),
    NotificationChannelId  INT           NOT NULL CONSTRAINT FK_tblNotificationRule_lkpNotificationChannel REFERENCES dbo.lkpNotificationChannel (Id),
    Target                 NVARCHAR(MAX) NOT NULL, -- email list, or protected Teams webhook
    IsEnabled              BIT           NOT NULL CONSTRAINT DF_tblNotificationRule_IsEnabled DEFAULT (1)
);
CREATE INDEX IX_tblNotificationRule_Event ON dbo.tblNotificationRule (NotificationEventId, JobId) WHERE IsEnabled = 1;
GO

CREATE TABLE dbo.tblSlaRule (
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblSlaRule PRIMARY KEY,
    JobId                INT           NOT NULL CONSTRAINT FK_tblSlaRule_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE,
    ExpectedByLocalTime  TIME(0)       NOT NULL,
    DaysMask             INT           NOT NULL CONSTRAINT DF_tblSlaRule_DaysMask DEFAULT (62),
    FilePattern          NVARCHAR(300) NULL,
    MinFileCount         INT           NOT NULL CONSTRAINT DF_tblSlaRule_MinFileCount DEFAULT (1),
    IsEnabled            BIT           NOT NULL CONSTRAINT DF_tblSlaRule_IsEnabled DEFAULT (1),
    LastBreachDate       DATE          NULL
);
GO

CREATE TABLE dbo.tblBlackoutWindow (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblBlackoutWindow PRIMARY KEY,
    JobId     INT           NULL CONSTRAINT FK_tblBlackoutWindow_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE, -- NULL = global
    StartUtc  DATETIME2(0)  NOT NULL,
    EndUtc    DATETIME2(0)  NOT NULL,
    Reason    NVARCHAR(300) NULL,
    CONSTRAINT CK_tblBlackoutWindow_Range CHECK (EndUtc > StartUtc)
);
CREATE INDEX IX_tblBlackoutWindow_Range ON dbo.tblBlackoutWindow (EndUtc, StartUtc) INCLUDE (JobId);
GO

CREATE TABLE dbo.tblGlobalSetting (
    SettingKey    NVARCHAR(100) NOT NULL CONSTRAINT PK_tblGlobalSetting PRIMARY KEY,
    SettingValue  NVARCHAR(MAX) NULL,
    Description   NVARCHAR(400) NULL,
    IsSecret      BIT           NOT NULL CONSTRAINT DF_tblGlobalSetting_IsSecret DEFAULT (0),
    CreatedUtc    DATETIME2(3)  NOT NULL,
    CreatedBy     NVARCHAR(256) NOT NULL,
    ModifiedUtc   DATETIME2(3)  NULL,
    ModifiedBy    NVARCHAR(256) NULL,
    RowVersion    ROWVERSION    NOT NULL
);
GO

CREATE TABLE dbo.tblRoleMapping (
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblRoleMapping PRIMARY KEY,
    AdGroup      NVARCHAR(256) NOT NULL,
    AppRoleId    INT           NOT NULL CONSTRAINT FK_tblRoleMapping_lkpAppRole REFERENCES dbo.lkpAppRole (Id),
    CreatedUtc   DATETIME2(3)  NOT NULL,
    CreatedBy    NVARCHAR(256) NOT NULL,
    ModifiedUtc  DATETIME2(3)  NULL,
    ModifiedBy   NVARCHAR(256) NULL,
    RowVersion   ROWVERSION    NOT NULL,
    CONSTRAINT UQ_tblRoleMapping_Group_Role UNIQUE (AdGroup, AppRoleId)
);
GO

-- ---------- Operational (high volume, not audited) ----------

CREATE TABLE dbo.tblTransferHistory (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblTransferHistory PRIMARY KEY,
    JobId             INT              NOT NULL CONSTRAINT FK_tblTransferHistory_tblJob REFERENCES dbo.tblJob (Id),
    RunId             UNIQUEIDENTIFIER NOT NULL,
    CorrelationId     NVARCHAR(64)     NOT NULL,
    SourcePath        NVARCHAR(1000)   NOT NULL,
    DestinationPath   NVARCHAR(2000)   NULL,
    FileName          NVARCHAR(400)    NOT NULL,
    SizeBytes         BIGINT           NOT NULL,
    Sha256            NVARCHAR(64)     NULL,
    TransferStatusId  INT              NOT NULL CONSTRAINT FK_tblTransferHistory_lkpTransferStatus REFERENCES dbo.lkpTransferStatus (Id),
    AttemptCount      INT              NOT NULL CONSTRAINT DF_tblTransferHistory_AttemptCount DEFAULT (0),
    StartedUtc        DATETIME2(3)     NOT NULL,
    CompletedUtc      DATETIME2(3)     NULL,
    DurationMs        BIGINT           NULL,
    ErrorMessage      NVARCHAR(4000)   NULL,
    NodeName          NVARCHAR(100)    NOT NULL,
    TriggeredBy       NVARCHAR(300)    NOT NULL
);
CREATE INDEX IX_tblTransferHistory_JobId_StartedUtc ON dbo.tblTransferHistory (JobId, StartedUtc DESC) INCLUDE (TransferStatusId, FileName, SizeBytes);
CREATE INDEX IX_tblTransferHistory_StartedUtc ON dbo.tblTransferHistory (StartedUtc) INCLUDE (TransferStatusId, SizeBytes, JobId);
CREATE INDEX IX_tblTransferHistory_InProgress ON dbo.tblTransferHistory (StartedUtc) WHERE TransferStatusId = 2;
CREATE INDEX IX_tblTransferHistory_Status_Id ON dbo.tblTransferHistory (TransferStatusId, Id DESC);
GO

CREATE TABLE dbo.tblFileLease (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblFileLease PRIMARY KEY,
    JobId             INT            NOT NULL CONSTRAINT FK_tblFileLease_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE,
    Fingerprint       NVARCHAR(64)   NOT NULL,  -- SHA256(endpoint|path|size|mtime)
    SourcePath        NVARCHAR(1000) NOT NULL,
    LeasedBy          NVARCHAR(100)  NOT NULL,
    LeaseExpiresUtc   DATETIME2(3)   NOT NULL,
    CompletedUtc      DATETIME2(3)   NULL,
    TransferStatusId  INT            NOT NULL CONSTRAINT FK_tblFileLease_lkpTransferStatus REFERENCES dbo.lkpTransferStatus (Id),
    AttemptCount      INT            NOT NULL CONSTRAINT DF_tblFileLease_AttemptCount DEFAULT (1),
    CreatedUtc        DATETIME2(3)   NOT NULL,
    CONSTRAINT UQ_tblFileLease_Job_Fingerprint UNIQUE (JobId, Fingerprint)  -- exactly-once guard across nodes
);
CREATE INDEX IX_tblFileLease_Job_SourcePath ON dbo.tblFileLease (JobId, SourcePath);
CREATE INDEX IX_tblFileLease_CompletedUtc ON dbo.tblFileLease (CompletedUtc) WHERE CompletedUtc IS NOT NULL;
GO

CREATE TABLE dbo.tblQuarantine (
    Id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblQuarantine PRIMARY KEY,
    JobId               INT            NOT NULL CONSTRAINT FK_tblQuarantine_tblJob REFERENCES dbo.tblJob (Id),
    FolderMapId         INT            NULL CONSTRAINT FK_tblQuarantine_tblJobFolderMap REFERENCES dbo.tblJobFolderMap (Id) ON DELETE SET NULL,
    TransferHistoryId   BIGINT         NULL CONSTRAINT FK_tblQuarantine_tblTransferHistory REFERENCES dbo.tblTransferHistory (Id) ON DELETE SET NULL,
    OriginalPath        NVARCHAR(1000) NOT NULL,
    QuarantinePath      NVARCHAR(1000) NOT NULL,
    Reason              NVARCHAR(1000) NOT NULL,
    QuarantineStatusId  INT            NOT NULL CONSTRAINT FK_tblQuarantine_lkpQuarantineStatus REFERENCES dbo.lkpQuarantineStatus (Id),
    CreatedUtc          DATETIME2(3)   NOT NULL,
    ReviewedBy          NVARCHAR(256)  NULL,
    ReviewedUtc         DATETIME2(3)   NULL
);
CREATE INDEX IX_tblQuarantine_Status ON dbo.tblQuarantine (QuarantineStatusId, Id DESC);
CREATE INDEX IX_tblQuarantine_TransferHistoryId ON dbo.tblQuarantine (TransferHistoryId);
GO

CREATE TABLE dbo.tblRunRequest (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblRunRequest PRIMARY KEY,
    RequestTypeId    INT            NOT NULL CONSTRAINT FK_tblRunRequest_lkpRequestType REFERENCES dbo.lkpRequestType (Id),
    JobId            INT            NULL CONSTRAINT FK_tblRunRequest_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE,
    EndpointId       INT            NULL CONSTRAINT FK_tblRunRequest_tblEndpoint REFERENCES dbo.tblEndpoint (Id) ON DELETE CASCADE,
    QuarantineId     INT            NULL CONSTRAINT FK_tblRunRequest_tblQuarantine REFERENCES dbo.tblQuarantine (Id) ON DELETE SET NULL,
    Path             NVARCHAR(1000) NULL,
    RequestStatusId  INT            NOT NULL CONSTRAINT FK_tblRunRequest_lkpRequestStatus REFERENCES dbo.lkpRequestStatus (Id),
    RequestedBy      NVARCHAR(256)  NOT NULL,
    RequestedUtc     DATETIME2(3)   NOT NULL,
    PickedBy         NVARCHAR(100)  NULL,
    StartedUtc       DATETIME2(3)   NULL,
    CompletedUtc     DATETIME2(3)   NULL,
    ResultJson       NVARCHAR(MAX)  NULL
);
CREATE INDEX IX_tblRunRequest_Status_Id ON dbo.tblRunRequest (RequestStatusId, Id);
GO

CREATE TABLE dbo.tblNodeHeartbeat (
    NodeName     NVARCHAR(100) NOT NULL,
    NodeRole     NVARCHAR(20)  NOT NULL,   -- Admin | Worker
    LastSeenUtc  DATETIME2(3)  NOT NULL,
    StartedUtc   DATETIME2(3)  NOT NULL,
    AppVersion   NVARCHAR(50)  NULL,
    CONSTRAINT PK_tblNodeHeartbeat PRIMARY KEY (NodeName, NodeRole)
);
GO

CREATE TABLE dbo.tblJobVersion (
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblJobVersion PRIMARY KEY,
    JobId         INT           NOT NULL CONSTRAINT FK_tblJobVersion_tblJob REFERENCES dbo.tblJob (Id) ON DELETE CASCADE,
    Version       INT           NOT NULL,
    SnapshotJson  NVARCHAR(MAX) NOT NULL,
    CreatedBy     NVARCHAR(256) NOT NULL,
    CreatedUtc    DATETIME2(3)  NOT NULL,
    CONSTRAINT UQ_tblJobVersion_Job_Version UNIQUE (JobId, Version)
);
GO

CREATE TABLE dbo.tblChangeRequest (
    Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblChangeRequest PRIMARY KEY,
    EntityName        NVARCHAR(100)  NOT NULL,
    EntityKey         NVARCHAR(100)  NULL,
    PayloadJson       NVARCHAR(MAX)  NOT NULL,
    ApprovalStatusId  INT            NOT NULL CONSTRAINT FK_tblChangeRequest_lkpApprovalStatus REFERENCES dbo.lkpApprovalStatus (Id),
    RequestedBy       NVARCHAR(256)  NOT NULL,
    RequestedUtc      DATETIME2(3)   NOT NULL,
    ReviewedBy        NVARCHAR(256)  NULL,
    ReviewedUtc       DATETIME2(3)   NULL,
    ReviewComment     NVARCHAR(1000) NULL,
    CONSTRAINT CK_tblChangeRequest_FourEyes CHECK (ReviewedBy IS NULL OR ApprovalStatusId = 3 OR ReviewedBy <> RequestedBy)
);
CREATE INDEX IX_tblChangeRequest_Status ON dbo.tblChangeRequest (ApprovalStatusId, Id DESC);
GO

CREATE TABLE dbo.tblConfigAudit (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblConfigAudit PRIMARY KEY,
    EntityName     NVARCHAR(128) NOT NULL,
    EntityKey      NVARCHAR(200) NOT NULL,
    AuditActionId  INT           NOT NULL CONSTRAINT FK_tblConfigAudit_lkpAuditAction REFERENCES dbo.lkpAuditAction (Id),
    BeforeJson     NVARCHAR(MAX) NULL,
    AfterJson      NVARCHAR(MAX) NULL,
    ChangedBy      NVARCHAR(256) NOT NULL,
    ChangedUtc     DATETIME2(3)  NOT NULL,
    Host           NVARCHAR(100) NOT NULL
);
CREATE INDEX IX_tblConfigAudit_ChangedUtc ON dbo.tblConfigAudit (ChangedUtc DESC);
CREATE INDEX IX_tblConfigAudit_Entity ON dbo.tblConfigAudit (EntityName, Id DESC);
GO

-- ASP.NET Core Data Protection key ring shared by every Admin and Worker node (keys wrapped by an X.509 cert).
CREATE TABLE dbo.tblDataProtectionKey (
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblDataProtectionKey PRIMARY KEY,
    FriendlyName  NVARCHAR(MAX) NULL,
    Xml           NVARCHAR(MAX) NULL
);
GO

-- Serilog MSSqlServer sink target (autoCreateSqlTable = false).
CREATE TABLE dbo.tblApplicationLog (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblApplicationLog PRIMARY KEY,
    Message          NVARCHAR(MAX) NULL,
    MessageTemplate  NVARCHAR(MAX) NULL,
    Level            NVARCHAR(16)  NULL,
    TimeStamp        DATETIME2(3)  NOT NULL,
    Exception        NVARCHAR(MAX) NULL,
    LogEvent         NVARCHAR(MAX) NULL
);
CREATE INDEX IX_tblApplicationLog_TimeStamp ON dbo.tblApplicationLog (TimeStamp);
GO
