-- =============================================================
-- Least-privilege database access for the service accounts (gMSA).
-- Replace AGENCY\gmsa-fbadmin$ and AGENCY\gmsa-fbworker$ with your accounts.
-- =============================================================
USE [master];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'AGENCY\gmsa-fbadmin$')  CREATE LOGIN [AGENCY\gmsa-fbadmin$]  FROM WINDOWS;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'AGENCY\gmsa-fbworker$') CREATE LOGIN [AGENCY\gmsa-fbworker$] FROM WINDOWS;
GO
USE [FileBridge];
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'FileBridgeApp') CREATE ROLE FileBridgeApp;
GO
-- Read everything; lookups are read-only for the app.
GRANT SELECT ON SCHEMA::dbo TO FileBridgeApp;
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'GRANT INSERT, UPDATE, DELETE ON dbo.' + QUOTENAME(name) + N' TO FileBridgeApp;' + CHAR(10)
FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo') AND name LIKE N'tbl%' AND name <> N'tblConfigAudit';
EXEC sys.sp_executesql @sql;
GRANT INSERT ON dbo.tblConfigAudit TO FileBridgeApp;   -- audit is append-only for the app (no UPDATE, no DELETE)
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'AGENCY\gmsa-fbadmin$')  CREATE USER [AGENCY\gmsa-fbadmin$]  FOR LOGIN [AGENCY\gmsa-fbadmin$];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'AGENCY\gmsa-fbworker$') CREATE USER [AGENCY\gmsa-fbworker$] FOR LOGIN [AGENCY\gmsa-fbworker$];
ALTER ROLE FileBridgeApp ADD MEMBER [AGENCY\gmsa-fbadmin$];
ALTER ROLE FileBridgeApp ADD MEMBER [AGENCY\gmsa-fbworker$];
-- Only the Worker (retention job) may purge audit rows past Retention.AuditDays.
GRANT DELETE ON dbo.tblConfigAudit TO [AGENCY\gmsa-fbworker$];
GO
