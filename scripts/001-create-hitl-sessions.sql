USE AgenticPoC;
GO

IF OBJECT_ID(N'dbo.HitlSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HitlSessions (
        SessionId UNIQUEIDENTIFIER PRIMARY KEY,
        Reason NVARCHAR(500) NOT NULL,
        PendingActionPayload NVARCHAR(MAX) NOT NULL,
        Status NVARCHAR(32) NOT NULL,
        SuspendedAt DATETIMEOFFSET NOT NULL
    );
END;
GO
