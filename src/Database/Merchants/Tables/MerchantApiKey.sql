-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Merchants.MerchantApiKey
-- Purpose: Keys a merchant's website sends on every API call, formatted od_{Prefix}_{secret}. The prefix is kept
--          in clear and is globally unique because it is how a key is found before the tenant is known; the
--          secret is only stored as a SHA-256 hash. A request with a key selects the key's tenant and merchant.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Merchants].[MerchantApiKey] (
    [Id]         BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT         NOT NULL,
    [MerchantId] BIGINT         NOT NULL,
    [Name]       NVARCHAR (100) NOT NULL,
    [Prefix]     NVARCHAR (12)  NOT NULL,
    [KeyHash]    BINARY (32)    NOT NULL,
    [LastUsedOn] DATETIME2 (0)  NULL,
    [RevokedOn]  DATETIME2 (0)  NULL,
    [UpdatedId]  BIGINT         NULL,
    [UpdatedOn]  DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_MerchantApiKey_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_MerchantApiKey_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_MerchantApiKey_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_MerchantApiKey_Prefix]
    ON [Merchants].[MerchantApiKey]([Prefix] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_MerchantApiKey_Tenant_Merchant]
    ON [Merchants].[MerchantApiKey]([TenantId] ASC, [MerchantId] ASC);
