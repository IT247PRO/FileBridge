-- =============================================================
-- FileBridge lookup seed data. Idempotent (MERGE). Generated from Enums.cs.
-- =============================================================
SET NOCOUNT ON;
GO

MERGE dbo.lkpEndpointType AS t
USING (VALUES
        (1, N'Smb', N'Smb', 10),
        (2, N'Sftp', N'Sftp', 20),
        (3, N'Https', N'Https', 30),
        (4, N'LocalDisk', N'Local Disk', 40)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpTransferDirection AS t
USING (VALUES
        (1, N'Inbound', N'Inbound', 10),
        (2, N'Outbound', N'Outbound', 20)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpTransferStatus AS t
USING (VALUES
        (1, N'Pending', N'Pending', 10),
        (2, N'InProgress', N'In Progress', 20),
        (3, N'Succeeded', N'Succeeded', 30),
        (4, N'Failed', N'Failed', 40),
        (5, N'Skipped', N'Skipped', 50),
        (6, N'Quarantined', N'Quarantined', 60),
        (7, N'Duplicate', N'Duplicate', 70),
        (8, N'DryRun', N'Dry Run', 80)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpPostActionType AS t
USING (VALUES
        (1, N'None', N'None', 10),
        (2, N'Delete', N'Delete', 20),
        (3, N'Archive', N'Archive', 30),
        (4, N'Rename', N'Rename', 40)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpSemaphoreMode AS t
USING (VALUES
        (1, N'None', N'None', 10),
        (2, N'PerFile', N'Per File', 20),
        (3, N'Batch', N'Batch', 30)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpEncryptionOperation AS t
USING (VALUES
        (1, N'None', N'None', 10),
        (2, N'PgpEncrypt', N'Pgp Encrypt', 20),
        (3, N'PgpDecrypt', N'Pgp Decrypt', 30),
        (4, N'PgpEncryptAndSign', N'Pgp Encrypt And Sign', 40),
        (5, N'PgpDecryptAndVerify', N'Pgp Decrypt And Verify', 50),
        (6, N'AesEncrypt', N'Aes Encrypt', 60),
        (7, N'AesDecrypt', N'Aes Decrypt', 70)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpCompressionOperation AS t
USING (VALUES
        (1, N'None', N'None', 10),
        (2, N'Zip', N'Zip', 20),
        (3, N'Unzip', N'Unzip', 30)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpDuplicatePolicy AS t
USING (VALUES
        (1, N'Skip', N'Skip', 10),
        (2, N'Overwrite', N'Overwrite', 20),
        (3, N'Version', N'Version', 30),
        (4, N'Fail', N'Fail', 40)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpScheduleType AS t
USING (VALUES
        (1, N'Cron', N'Cron', 10),
        (2, N'Interval', N'Interval', 20)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpNotificationChannel AS t
USING (VALUES
        (1, N'Email', N'Email', 10),
        (2, N'Teams', N'Teams', 20)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpNotificationEvent AS t
USING (VALUES
        (1, N'JobFailed', N'Job Failed', 10),
        (2, N'FileQuarantined', N'File Quarantined', 20),
        (3, N'SlaBreach', N'Sla Breach', 30),
        (4, N'JobSucceeded', N'Job Succeeded', 40),
        (5, N'ApprovalRequested', N'Approval Requested', 50)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpApprovalStatus AS t
USING (VALUES
        (1, N'Pending', N'Pending', 10),
        (2, N'Approved', N'Approved', 20),
        (3, N'Rejected', N'Rejected', 30)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpRequestType AS t
USING (VALUES
        (1, N'RunNow', N'Run Now', 10),
        (2, N'DryRun', N'Dry Run', 20),
        (3, N'TestConnection', N'Test Connection', 30),
        (4, N'Browse', N'Browse', 40),
        (5, N'ReleaseQuarantine', N'Release Quarantine', 50),
        (6, N'DiscardQuarantine', N'Discard Quarantine', 60)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpRequestStatus AS t
USING (VALUES
        (1, N'Queued', N'Queued', 10),
        (2, N'Running', N'Running', 20),
        (3, N'Completed', N'Completed', 30),
        (4, N'Failed', N'Failed', 40)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpQuarantineStatus AS t
USING (VALUES
        (1, N'Held', N'Held', 10),
        (2, N'Released', N'Released', 20),
        (3, N'Discarded', N'Discarded', 30)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpAppRole AS t
USING (VALUES
        (1, N'Viewer', N'Viewer', 10),
        (2, N'Operator', N'Operator', 20),
        (3, N'Admin', N'Admin', 30),
        (4, N'Approver', N'Approver', 40)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO

MERGE dbo.lkpAuditAction AS t
USING (VALUES
        (1, N'Added', N'Added', 10),
        (2, N'Modified', N'Modified', 20),
        (3, N'Deleted', N'Deleted', 30)
) AS s (Id, Code, Name, SortOrder)
ON t.Id = s.Id
WHEN MATCHED THEN UPDATE SET t.Code = s.Code, t.Name = s.Name, t.SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (Id, Code, Name, SortOrder) VALUES (s.Id, s.Code, s.Name, s.SortOrder);
GO
