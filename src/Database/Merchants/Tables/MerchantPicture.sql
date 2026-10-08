-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Merchants.MerchantPicture
-- Purpose: The picture (logo or photo) a business shows beside its name, as uploaded: PNG, JPEG or WebP, at most
--          512 KB. One row per merchant, a main profile and each business having its own. Kept out of
--          Merchants.Merchant so that reading a merchant never loads the bytes.
-- Author: Courier team
-- Date: 2026-10-06
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Merchants].[MerchantPicture] (
    [Id]          BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]    BIGINT          NOT NULL,
    [MerchantId]  BIGINT          NOT NULL,
    [ContentType] NVARCHAR (50)   NOT NULL,
    [Content]     VARBINARY (MAX) NOT NULL,
    [UpdatedId]   BIGINT          NULL,
    [UpdatedOn]   DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]     DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_MerchantPicture_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_MerchantPicture_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_MerchantPicture_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_MerchantPicture_Tenant_Merchant]
    ON [Merchants].[MerchantPicture]([TenantId] ASC, [MerchantId] ASC);
