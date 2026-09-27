-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Merchants.Merchant
-- Purpose: An online shop that offers our delivery at checkout. Merchants pay nothing. ZoneId is the zone whose
--          pickup route collects from them. Merchant users only ever see their own row and parcels.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Merchants].[Merchant] (
    [Id]               BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]         BIGINT         NOT NULL,
    [Name]             NVARCHAR (200) NOT NULL,
    [ZoneId]           BIGINT         NOT NULL,
    [ContactPhone]     NVARCHAR (20)  NOT NULL,
    [ContactEmail]     NVARCHAR (320) NULL,
    -- Late or missed handovers lower it; merchants that are late too often lose grouping
    [ReliabilityScore] INT            DEFAULT ((100)) NOT NULL,
    [Archived]         BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId]        BIGINT         NULL,
    [UpdatedOn]        DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]          DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Merchant_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Merchant_Zone] FOREIGN KEY ([ZoneId]) REFERENCES [Network].[Zone] ([Id]),
    CONSTRAINT [FK_Merchant_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Merchant_Tenant_Name]
    ON [Merchants].[Merchant]([TenantId] ASC, [Name] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Merchant_ZoneId]
    ON [Merchants].[Merchant]([ZoneId] ASC);
