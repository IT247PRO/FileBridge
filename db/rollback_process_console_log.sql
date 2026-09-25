-- =============================================================
-- Rollback for the (reverted) live process-console-log feature.
-- Safe to run whether or not you ever applied the earlier 02_tables.sql /
-- 04_seed_config.sql versions that added these -- every step is guarded.
-- =============================================================
SET NOCOUNT ON;
GO

IF COL_LENGTH(N'dbo.tblProcessRunHistory', N'ConsoleOutput') IS NOT NULL
BEGIN
    ALTER TABLE dbo.tblProcessRunHistory DROP COLUMN ConsoleOutput;
END
GO

IF COL_LENGTH(N'dbo.tblProcessRunHistory', N'ConsoleUpdatedUtc') IS NOT NULL
BEGIN
    ALTER TABLE dbo.tblProcessRunHistory DROP COLUMN ConsoleUpdatedUtc;
END
GO

IF EXISTS (SELECT 1 FROM dbo.tblGlobalSetting WHERE SettingKey = N'Retention.ProcessConsoleHours')
BEGIN
    DELETE FROM dbo.tblGlobalSetting WHERE SettingKey = N'Retention.ProcessConsoleHours';
END
GO
