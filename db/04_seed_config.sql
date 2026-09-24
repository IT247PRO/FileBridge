-- =============================================================
-- Default global settings and the bootstrap admin group. Idempotent.
-- =============================================================
SET NOCOUNT ON;
GO

MERGE dbo.tblGlobalSetting AS t
USING (VALUES
    (N'Engine.KillSwitch',              N'false', N'Stop all scheduled transfers immediately (emergency stop).'),
    (N'Approval.RequireForJobChanges',  N'false', N'Require a second person to approve job changes (four-eyes).'),
    (N'Retention.HistoryDays',          N'400',   N'Days of transfer history to keep.'),
    (N'Retention.LeaseDays',            N'90',    N'Days to remember completed files (older unchanged files could be re-sent if still at the source).'),
    (N'Retention.RequestDays',          N'30',    N'Days to keep test, browse and run-now requests.'),
    (N'Retention.AuditDays',            N'2555',  N'Days of configuration audit to keep (7 years).'),
    (N'Retention.LogDays',              N'90',    N'Days of application log rows to keep in SQL.'),
    (N'Retention.QuarantineDays',       N'180',   N'Days to keep reviewed quarantine records and files.')
) AS s (SettingKey, SettingValue, Description)
ON t.SettingKey = s.SettingKey
WHEN MATCHED THEN UPDATE SET t.Description = s.Description
WHEN NOT MATCHED THEN INSERT (SettingKey, SettingValue, Description, IsSecret, CreatedUtc, CreatedBy)
    VALUES (s.SettingKey, s.SettingValue, s.Description, 0, SYSUTCDATETIME(), N'install');
GO

-- Replace with your AD groups. Security:BootstrapAdmins in appsettings also always grants Admin.
IF NOT EXISTS (SELECT 1 FROM dbo.tblRoleMapping WHERE AdGroup = N'AGENCY\FileBridge-Admins' AND AppRoleId = 3)
    INSERT dbo.tblRoleMapping (AdGroup, AppRoleId, CreatedUtc, CreatedBy) VALUES (N'AGENCY\FileBridge-Admins', 3, SYSUTCDATETIME(), N'install');
IF NOT EXISTS (SELECT 1 FROM dbo.tblRoleMapping WHERE AdGroup = N'AGENCY\FileBridge-Operators' AND AppRoleId = 2)
    INSERT dbo.tblRoleMapping (AdGroup, AppRoleId, CreatedUtc, CreatedBy) VALUES (N'AGENCY\FileBridge-Operators', 2, SYSUTCDATETIME(), N'install');
IF NOT EXISTS (SELECT 1 FROM dbo.tblRoleMapping WHERE AdGroup = N'AGENCY\FileBridge-Viewers' AND AppRoleId = 1)
    INSERT dbo.tblRoleMapping (AdGroup, AppRoleId, CreatedUtc, CreatedBy) VALUES (N'AGENCY\FileBridge-Viewers', 1, SYSUTCDATETIME(), N'install');
GO
