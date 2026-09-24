-- 01_lookups.sql: FileBridge Lookup Tables
CREATE TABLE lkpEndpointType (
    Id INT PRIMARY KEY,
    Code NVARCHAR(30) NOT NULL,
    Description NVARCHAR(200)
);

CREATE TABLE lkpTransferStatus (
    Id INT PRIMARY KEY,
    Code NVARCHAR(30) NOT NULL,
    Description NVARCHAR(200)
);

CREATE TABLE lkpJobTriggerType (
    Id INT PRIMARY KEY,
    Code NVARCHAR(30) NOT NULL,
    Description NVARCHAR(200)
);

CREATE TABLE lkpQuarantineReason (
    Id INT PRIMARY KEY,
    Code NVARCHAR(50) NOT NULL,
    Description NVARCHAR(200)
);

CREATE TABLE lkpQuarantineStatus (
    Id INT PRIMARY KEY,
    Code NVARCHAR(30) NOT NULL,
    Description NVARCHAR(200)
);
