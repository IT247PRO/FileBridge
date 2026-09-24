-- 02_tables.sql: FileBridge Core and Operational Tables
CREATE TABLE tblEndpoint (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    EndpointTypeId INT NOT NULL FOREIGN KEY REFERENCES lkpEndpointType(Id),
    Host NVARCHAR(255),
    Port INT,
    BasePath NVARCHAR(1000),
    Username NVARCHAR(100),
    EncryptedPassword NVARCHAR(MAX),
    EncryptedPrivateKey NVARCHAR(MAX),
    Notes NVARCHAR(MAX),
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE tblJob (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(MAX),
    IsEnabled BIT NOT NULL DEFAULT 1,
    SourceEndpointId INT NOT NULL FOREIGN KEY REFERENCES tblEndpoint(Id),
    SourcePath NVARCHAR(1000) NOT NULL,
    FilePattern NVARCHAR(100) NOT NULL DEFAULT '*.*',
    DestinationEndpointId INT NOT NULL FOREIGN KEY REFERENCES tblEndpoint(Id),
    DestinationPath NVARCHAR(1000) NOT NULL,
    CronExpression NVARCHAR(100) NOT NULL DEFAULT '0 0 * * * ?',
    TriggerTypeId INT NOT NULL FOREIGN KEY REFERENCES lkpJobTriggerType(Id),
    OverwriteDestination BIT NOT NULL DEFAULT 0,
    DeleteSourceAfterTransfer BIT NOT NULL DEFAULT 0,
    EncryptionProfileId INT NULL,
    RequireApproval BIT NOT NULL DEFAULT 0,
    MaxRetries INT NOT NULL DEFAULT 3,
    RetryBackoffSeconds INT NOT NULL DEFAULT 60,
    LastRunUtc DATETIME2 NULL,
    LastRunStatus INT NULL FOREIGN KEY REFERENCES lkpTransferStatus(Id),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE tblTransferHistory (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    JobId INT NOT NULL FOREIGN KEY REFERENCES tblJob(Id),
    CorrelationId NVARCHAR(50) NOT NULL,
    StatusId INT NOT NULL FOREIGN KEY REFERENCES lkpTransferStatus(Id),
    StartedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAtUtc DATETIME2 NULL,
    FilesTransferred INT NOT NULL DEFAULT 0,
    FilesFailed INT NOT NULL DEFAULT 0,
    BytesTransferred BIGINT NOT NULL DEFAULT 0,
    ErrorMessage NVARCHAR(MAX),
    ExecutionNode NVARCHAR(100)
);

CREATE TABLE tblQuarantine (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OriginalFileName NVARCHAR(255) NOT NULL,
    SourcePath NVARCHAR(1000) NOT NULL,
    QuarantinedFilePath NVARCHAR(1000) NOT NULL,
    ReasonId INT NOT NULL FOREIGN KEY REFERENCES lkpQuarantineReason(Id),
    StatusId INT NOT NULL DEFAULT 1 FOREIGN KEY REFERENCES lkpQuarantineStatus(Id),
    Sha256 NVARCHAR(64),
    FileSizeBytes BIGINT NOT NULL DEFAULT 0,
    QuarantinedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ReviewedBy NVARCHAR(100),
    ReviewedAtUtc DATETIME2
);

CREATE TABLE tblRunRequest (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    RequestType NVARCHAR(50) NOT NULL,
    PayloadJson NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    Status NVARCHAR(30) NOT NULL DEFAULT 'Pending',
    ClaimedByNode NVARCHAR(100),
    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAtUtc DATETIME2,
    ResultJson NVARCHAR(MAX)
);

CREATE TABLE tblGlobalSetting (
    [Key] NVARCHAR(100) PRIMARY KEY,
    [Value] NVARCHAR(MAX) NOT NULL,
    Description NVARCHAR(MAX),
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE tblRoleMapping (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PrincipalName NVARCHAR(255) NOT NULL,
    RoleTypeId INT NOT NULL
);
