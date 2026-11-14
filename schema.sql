CREATE DATABASE AgenticPoC;
GO

USE AgenticPoC;
GO

CREATE TABLE dbo.AgentSessions (
    SessionId UNIQUEIDENTIFIER PRIMARY KEY,
    UserId NVARCHAR(100) NOT NULL,
    Status NVARCHAR(50) NOT NULL,
    ChatHistoryJson NVARCHAR(MAX) NOT NULL,
    PendingActionPayload NVARCHAR(MAX) NULL,
    LastUpdated DATETIME NOT NULL
);
GO

CREATE TABLE dbo.HitlSessions (
    SessionId UNIQUEIDENTIFIER PRIMARY KEY,
    TenantId NVARCHAR(100) NOT NULL,
    Reason NVARCHAR(500) NOT NULL,
    PendingActionPayload NVARCHAR(MAX) NOT NULL,
    Status NVARCHAR(32) NOT NULL,
    SuspendedAt DATETIMEOFFSET NOT NULL,
    ExecutionAttempts INT NOT NULL CONSTRAINT DF_HitlSessions_ExecutionAttempts DEFAULT 0,
    ProcessingStartedAt DATETIMEOFFSET NULL
);
GO

CREATE INDEX IX_HitlSessions_WorkQueue
    ON dbo.HitlSessions (Status, SuspendedAt)
    INCLUDE (TenantId, ExecutionAttempts, ProcessingStartedAt);
GO

CREATE TABLE dbo.StaffAvailabilityOverrides (
    SessionId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    TenantId NVARCHAR(100) NOT NULL,
    StaffId NVARCHAR(100) NOT NULL,
    NewAvailability DATETIMEOFFSET NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL
);
GO
