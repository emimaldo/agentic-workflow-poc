-- Development seed data. Never run against a production database.
USE AgenticPoC;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Accounts WHERE TenantId = N'tenant-local' AND AccountId = N'OPS-EUR-001')
    INSERT INTO dbo.Accounts (TenantId, AccountId, Currency, Balance)
    VALUES (N'tenant-local', N'OPS-EUR-001', 'EUR', 50000.0000);
GO

-- Example of a sanctioned / blocked beneficiary (public sample IBAN).
IF NOT EXISTS (SELECT 1 FROM dbo.BlockedBeneficiaries WHERE TenantId = N'tenant-local' AND Iban = N'GB29NWBK60161331926819')
    INSERT INTO dbo.BlockedBeneficiaries (TenantId, Iban, Reason, BlockedAt)
    VALUES (N'tenant-local', N'GB29NWBK60161331926819', N'Sample sanctions-list hit', SYSDATETIMEOFFSET());
GO
