-- 004: Chapter 3 alignment — SerializedContext, operator audit, Bounded Re-evaluation outcome.
USE AgenticPoC;
GO

IF COL_LENGTH(N'dbo.HitlSessions', N'PendingActionPayload') IS NOT NULL
   AND COL_LENGTH(N'dbo.HitlSessions', N'SerializedContext') IS NULL
BEGIN
    EXEC sp_rename N'dbo.HitlSessions.PendingActionPayload', N'SerializedContext', N'COLUMN';
END;
GO

IF COL_LENGTH(N'dbo.HitlSessions', N'ResolvedBy') IS NULL
    ALTER TABLE dbo.HitlSessions ADD ResolvedBy NVARCHAR(200) NULL;
GO

IF COL_LENGTH(N'dbo.HitlSessions', N'ResolvedAt') IS NULL
    ALTER TABLE dbo.HitlSessions ADD ResolvedAt DATETIMEOFFSET NULL;
GO

-- Set when an approved session fails the fresh domain check before execution (Status = 'Invalidated').
IF COL_LENGTH(N'dbo.HitlSessions', N'InvalidationReason') IS NULL
    ALTER TABLE dbo.HitlSessions ADD InvalidationReason NVARCHAR(200) NULL;
GO
