-- 005: the HITL session key becomes (TenantId, SessionId).
-- A global SessionId key let one tenant's id collide with another's (500 + an existence oracle).
USE AgenticPoC;
GO

DECLARE @pk sysname, @keyColumns int, @sql nvarchar(400);

SELECT @pk = kc.name
FROM sys.key_constraints AS kc
WHERE kc.parent_object_id = OBJECT_ID(N'dbo.HitlSessions') AND kc.type = 'PK';

SELECT @keyColumns = COUNT(*)
FROM sys.key_constraints AS kc
JOIN sys.index_columns AS ic
    ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
WHERE kc.name = @pk AND kc.parent_object_id = OBJECT_ID(N'dbo.HitlSessions');

IF @keyColumns = 1
BEGIN
    SET @sql = N'ALTER TABLE dbo.HitlSessions DROP CONSTRAINT ' + QUOTENAME(@pk) + N';';
    EXEC sys.sp_executesql @sql;
    EXEC sys.sp_executesql N'ALTER TABLE dbo.HitlSessions ADD CONSTRAINT PK_HitlSessions PRIMARY KEY (TenantId, SessionId);';
END;
GO
