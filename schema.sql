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

CREATE TABLE dbo.Accounts (
    TenantId NVARCHAR(100) NOT NULL,
    AccountId NVARCHAR(64) NOT NULL,
    Currency CHAR(3) NOT NULL,
    Balance DECIMAL(19, 4) NOT NULL,
    IsFrozen BIT NOT NULL CONSTRAINT DF_Accounts_IsFrozen DEFAULT 0,
    CONSTRAINT PK_Accounts PRIMARY KEY (TenantId, AccountId),
    CONSTRAINT CK_Accounts_Balance_NonNegative CHECK (Balance >= 0)
);
GO

CREATE TABLE dbo.Transfers (
    TenantId NVARCHAR(100) NOT NULL,
    OperationId UNIQUEIDENTIFIER NOT NULL,
    SourceAccountId NVARCHAR(64) NOT NULL,
    DestinationAccount NVARCHAR(34) NOT NULL,
    Amount DECIMAL(19, 4) NOT NULL,
    Currency CHAR(3) NOT NULL,
    ExecutedAt DATETIMEOFFSET NOT NULL,
    CONSTRAINT PK_Transfers PRIMARY KEY (TenantId, OperationId),
    CONSTRAINT FK_Transfers_Accounts FOREIGN KEY (TenantId, SourceAccountId)
        REFERENCES dbo.Accounts (TenantId, AccountId),
    CONSTRAINT CK_Transfers_Amount_Positive CHECK (Amount > 0)
);
GO

CREATE INDEX IX_Transfers_DailyDebits
    ON dbo.Transfers (TenantId, SourceAccountId, ExecutedAt)
    INCLUDE (Amount);
GO

CREATE TABLE dbo.BlockedBeneficiaries (
    TenantId NVARCHAR(100) NOT NULL,
    Iban NVARCHAR(34) NOT NULL,
    Reason NVARCHAR(200) NULL,
    BlockedAt DATETIMEOFFSET NOT NULL,
    CONSTRAINT PK_BlockedBeneficiaries PRIMARY KEY (TenantId, Iban)
);
GO
