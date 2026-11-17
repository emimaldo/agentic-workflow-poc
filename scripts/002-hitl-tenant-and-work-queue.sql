USE AgenticPoC;
GO

IF COL_LENGTH(N'dbo.HitlSessions', N'TenantId') IS NULL
BEGIN
    ALTER TABLE dbo.HitlSessions
        ADD TenantId NVARCHAR(100) NOT NULL
            CONSTRAINT DF_HitlSessions_TenantId DEFAULT N'legacy' WITH VALUES;
END;
GO

IF COL_LENGTH(N'dbo.HitlSessions', N'ExecutionAttempts') IS NULL
BEGIN
    ALTER TABLE dbo.HitlSessions
        ADD ExecutionAttempts INT NOT NULL
            CONSTRAINT DF_HitlSessions_ExecutionAttempts DEFAULT 0 WITH VALUES;
END;
GO

IF COL_LENGTH(N'dbo.HitlSessions', N'ProcessingStartedAt') IS NULL
BEGIN
    ALTER TABLE dbo.HitlSessions ADD ProcessingStartedAt DATETIMEOFFSET NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.HitlSessions')
      AND name = N'IX_HitlSessions_WorkQueue')
BEGIN
    CREATE INDEX IX_HitlSessions_WorkQueue
        ON dbo.HitlSessions (Status, SuspendedAt)
        INCLUDE (TenantId, ExecutionAttempts, ProcessingStartedAt);
END;
GO

IF OBJECT_ID(N'dbo.StaffAvailabilityOverrides', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffAvailabilityOverrides (
        SessionId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        TenantId NVARCHAR(100) NOT NULL,
        StaffId NVARCHAR(100) NOT NULL,
        NewAvailability DATETIMEOFFSET NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL
    );
END;
GO
