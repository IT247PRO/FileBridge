-- =============================================================
-- FileBridge lookup tables (lkp*). Generated from Enums.cs.
-- Run order: 01_lookups -> 02_tables -> 03_seed_lookups -> 04_seed_config -> 05_quartz -> 06_security
-- =============================================================
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.lkpEndpointType', N'U') IS NULL
CREATE TABLE dbo.lkpEndpointType (
    Id          INT            NOT NULL CONSTRAINT PK_lkpEndpointType PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpEndpointType_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpEndpointType_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpEndpointType_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpTransferDirection', N'U') IS NULL
CREATE TABLE dbo.lkpTransferDirection (
    Id          INT            NOT NULL CONSTRAINT PK_lkpTransferDirection PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpTransferDirection_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpTransferDirection_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpTransferDirection_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpTransferStatus', N'U') IS NULL
CREATE TABLE dbo.lkpTransferStatus (
    Id          INT            NOT NULL CONSTRAINT PK_lkpTransferStatus PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpTransferStatus_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpTransferStatus_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpTransferStatus_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpPostActionType', N'U') IS NULL
CREATE TABLE dbo.lkpPostActionType (
    Id          INT            NOT NULL CONSTRAINT PK_lkpPostActionType PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpPostActionType_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpPostActionType_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpPostActionType_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpSemaphoreMode', N'U') IS NULL
CREATE TABLE dbo.lkpSemaphoreMode (
    Id          INT            NOT NULL CONSTRAINT PK_lkpSemaphoreMode PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpSemaphoreMode_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpSemaphoreMode_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpSemaphoreMode_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpEncryptionOperation', N'U') IS NULL
CREATE TABLE dbo.lkpEncryptionOperation (
    Id          INT            NOT NULL CONSTRAINT PK_lkpEncryptionOperation PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpEncryptionOperation_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpEncryptionOperation_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpEncryptionOperation_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpCompressionOperation', N'U') IS NULL
CREATE TABLE dbo.lkpCompressionOperation (
    Id          INT            NOT NULL CONSTRAINT PK_lkpCompressionOperation PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpCompressionOperation_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpCompressionOperation_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpCompressionOperation_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpDuplicatePolicy', N'U') IS NULL
CREATE TABLE dbo.lkpDuplicatePolicy (
    Id          INT            NOT NULL CONSTRAINT PK_lkpDuplicatePolicy PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpDuplicatePolicy_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpDuplicatePolicy_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpDuplicatePolicy_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpScheduleType', N'U') IS NULL
CREATE TABLE dbo.lkpScheduleType (
    Id          INT            NOT NULL CONSTRAINT PK_lkpScheduleType PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpScheduleType_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpScheduleType_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpScheduleType_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpNotificationChannel', N'U') IS NULL
CREATE TABLE dbo.lkpNotificationChannel (
    Id          INT            NOT NULL CONSTRAINT PK_lkpNotificationChannel PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpNotificationChannel_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpNotificationChannel_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpNotificationChannel_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpNotificationEvent', N'U') IS NULL
CREATE TABLE dbo.lkpNotificationEvent (
    Id          INT            NOT NULL CONSTRAINT PK_lkpNotificationEvent PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpNotificationEvent_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpNotificationEvent_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpNotificationEvent_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpApprovalStatus', N'U') IS NULL
CREATE TABLE dbo.lkpApprovalStatus (
    Id          INT            NOT NULL CONSTRAINT PK_lkpApprovalStatus PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpApprovalStatus_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpApprovalStatus_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpApprovalStatus_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpRequestType', N'U') IS NULL
CREATE TABLE dbo.lkpRequestType (
    Id          INT            NOT NULL CONSTRAINT PK_lkpRequestType PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpRequestType_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpRequestType_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpRequestType_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpRequestStatus', N'U') IS NULL
CREATE TABLE dbo.lkpRequestStatus (
    Id          INT            NOT NULL CONSTRAINT PK_lkpRequestStatus PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpRequestStatus_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpRequestStatus_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpRequestStatus_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpQuarantineStatus', N'U') IS NULL
CREATE TABLE dbo.lkpQuarantineStatus (
    Id          INT            NOT NULL CONSTRAINT PK_lkpQuarantineStatus PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpQuarantineStatus_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpQuarantineStatus_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpQuarantineStatus_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpAppRole', N'U') IS NULL
CREATE TABLE dbo.lkpAppRole (
    Id          INT            NOT NULL CONSTRAINT PK_lkpAppRole PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpAppRole_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpAppRole_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpAppRole_IsActive DEFAULT (1)
);
GO

IF OBJECT_ID(N'dbo.lkpAuditAction', N'U') IS NULL
CREATE TABLE dbo.lkpAuditAction (
    Id          INT            NOT NULL CONSTRAINT PK_lkpAuditAction PRIMARY KEY,
    Code        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_lkpAuditAction_Code UNIQUE,
    Name        NVARCHAR(100)  NOT NULL,
    Description NVARCHAR(400)  NULL,
    SortOrder   INT            NOT NULL CONSTRAINT DF_lkpAuditAction_SortOrder DEFAULT (0),
    IsActive    BIT            NOT NULL CONSTRAINT DF_lkpAuditAction_IsActive DEFAULT (1)
);
GO
