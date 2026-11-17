-- 003: FinTech ledger (Chapters 2-3). Replaces the staff-availability demo domain.
USE AgenticPoC;
GO

IF OBJECT_ID(N'dbo.Accounts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Accounts (
        TenantId NVARCHAR(100) NOT NULL,
        AccountId NVARCHAR(64) NOT NULL,
        Currency CHAR(3) NOT NULL,
        Balance DECIMAL(19, 4) NOT NULL,
        IsFrozen BIT NOT NULL CONSTRAINT DF_Accounts_IsFrozen DEFAULT 0,
        CONSTRAINT PK_Accounts PRIMARY KEY (TenantId, AccountId),
        -- Last line of defense: the database itself refuses an overdraft.
        CONSTRAINT CK_Accounts_Balance_NonNegative CHECK (Balance >= 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.Transfers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Transfers (
        TenantId NVARCHAR(100) NOT NULL,
        OperationId UNIQUEIDENTIFIER NOT NULL, -- idempotency key (= HITL session id)
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

    CREATE INDEX IX_Transfers_DailyDebits
        ON dbo.Transfers (TenantId, SourceAccountId, ExecutedAt)
        INCLUDE (Amount);
END;
GO

IF OBJECT_ID(N'dbo.BlockedBeneficiaries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BlockedBeneficiaries (
        TenantId NVARCHAR(100) NOT NULL,
        Iban NVARCHAR(34) NOT NULL,
        Reason NVARCHAR(200) NULL,
        BlockedAt DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_BlockedBeneficiaries PRIMARY KEY (TenantId, Iban)
    );
END;
GO

-- The staff-availability demo table only ever held demonstration data.
DROP TABLE IF EXISTS dbo.StaffAvailabilityOverrides;
GO
