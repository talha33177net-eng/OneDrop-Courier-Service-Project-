-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Merchants.Merchant
-- Purpose: A shop that sends parcels with the courier. Status is a TINYINT enum (Domain.Merchants.MerchantStatus):
--          1 Pending (signed up, waiting for approval), 2 Active, 3 Suspended; only an active merchant books parcels.
--          PayoutMethod (1 bKash, 2 Nagad, 3 Bank) and PayoutAccount say where payouts go. WebhookUrl and
--          WebhookSecret: where the shop's parcel status changes are posted, and the whsec_ secret they are signed
--          with (kept as it is: signing needs it). Merchant users only ever see their own row and parcels.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Merchants].[Merchant] (
    [Id]                BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]          BIGINT         NOT NULL,
    [Name]              NVARCHAR (200) NOT NULL,
    [OwnerName]         NVARCHAR (200) NOT NULL,
    [ContactPhone]      NVARCHAR (20)  NOT NULL,
    [ContactEmail]      NVARCHAR (320) NULL,
    [Address]           NVARCHAR (500) NOT NULL,
    [Status]            TINYINT        NOT NULL,
    [PayoutMethod]      TINYINT        NULL,
    [PayoutAccount]     NVARCHAR (30)  NULL,
    [PayoutAccountName] NVARCHAR (200) NULL,
    [WebhookUrl]        NVARCHAR (500) NULL,
    [WebhookSecret]     NVARCHAR (100) NULL,
    [Archived]          BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId]         BIGINT         NULL,
    [UpdatedOn]         DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]           DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Merchant_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Merchant_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Merchant_Status] CHECK ([Status] BETWEEN 1 AND 3),
    CONSTRAINT [chk_Merchant_Payout] CHECK (([PayoutMethod] IS NULL AND [PayoutAccount] IS NULL) OR ([PayoutMethod] BETWEEN 1 AND 3 AND [PayoutAccount] IS NOT NULL)),
    CONSTRAINT [chk_Merchant_Webhook] CHECK ([WebhookUrl] IS NULL OR [WebhookSecret] IS NOT NULL)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Merchant_Tenant_Name]
    ON [Merchants].[Merchant]([TenantId] ASC, [Name] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Merchant_Tenant_Status]
    ON [Merchants].[Merchant]([TenantId] ASC, [Status] ASC);
