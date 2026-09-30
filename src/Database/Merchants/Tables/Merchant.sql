-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Merchants.Merchant
-- Purpose: An online shop that offers our delivery at checkout. Merchants pay nothing. ZoneId is the zone whose
--          pickup route collects from them. Merchant users only ever see their own row and parcels.
-- Author: Courier team
-- Date: 2026-09-27
-- 2026-09-30: ReliabilityScore dropped (task 3.8, Pre/004): whether a shop brings its parcels to the hub is worked
--             out from its recent late handovers (Orders.Order.ShopLateOn)
-- 2026-09-30: WebhookUrl and WebhookSecret (task 4.2): where the shop's order status changes are posted, and the
--             whsec_ secret they are signed with (kept as it is: signing needs it)
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Merchants].[Merchant] (
    [Id]            BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]      BIGINT         NOT NULL,
    [Name]          NVARCHAR (200) NOT NULL,
    [ZoneId]        BIGINT         NOT NULL,
    [ContactPhone]  NVARCHAR (20)  NOT NULL,
    [ContactEmail]  NVARCHAR (320) NULL,
    [WebhookUrl]    NVARCHAR (500) NULL,
    [WebhookSecret] NVARCHAR (100) NULL,
    [Archived]      BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId]     BIGINT         NULL,
    [UpdatedOn]     DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]       DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Merchant_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Merchant_Zone] FOREIGN KEY ([ZoneId]) REFERENCES [Network].[Zone] ([Id]),
    CONSTRAINT [FK_Merchant_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Merchant_Webhook] CHECK ([WebhookUrl] IS NULL OR [WebhookSecret] IS NOT NULL)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Merchant_Tenant_Name]
    ON [Merchants].[Merchant]([TenantId] ASC, [Name] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Merchant_ZoneId]
    ON [Merchants].[Merchant]([ZoneId] ASC);
