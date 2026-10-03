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
    Reason NVARCHAR(500) NOT NULL,
    PendingActionPayload NVARCHAR(MAX) NOT NULL,
    Status NVARCHAR(32) NOT NULL,
    SuspendedAt DATETIMEOFFSET NOT NULL
);
GO
