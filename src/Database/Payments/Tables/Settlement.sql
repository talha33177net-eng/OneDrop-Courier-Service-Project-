-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Payments.Settlement
-- Purpose: A payout to a shop: the Payments.LedgerEntry lines not yet paid out up to UpToDate (the tenant's day), when
--          they come to more than nothing. Sent to Account (the shop's bKash number) through the payout gateway.
--          Status is a TINYINT enum (Domain.Payments.SettlementStatus): 1 Pending until the gateway confirms, 2 Paid
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Payments].[Settlement] (
    [Id]               BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]         BIGINT          NOT NULL,
    [MerchantId]       BIGINT          NOT NULL,
    [UpToDate]         DATE            NOT NULL,
    [Amount]           DECIMAL (12, 2) NOT NULL,
    [Status]           TINYINT         NOT NULL,
    [Account]          NVARCHAR (20)   NOT NULL,
    [GatewayReference] NVARCHAR (100)  NULL,
    [PaidOn]           DATETIME2 (7)   NULL,
    [RowVersion]       ROWVERSION      NOT NULL,
    [UpdatedId]        BIGINT          NULL,
    [UpdatedOn]        DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]          DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Settlement_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Settlement_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_Settlement_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Settlement_Status] CHECK ([Status] BETWEEN 1 AND 2),
    CONSTRAINT [chk_Settlement_Amount] CHECK ([Amount] > (0))
);


GO
CREATE NONCLUSTERED INDEX [IX_Settlement_Tenant_Status]
    ON [Payments].[Settlement]([TenantId] ASC, [Status] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Settlement_Merchant_UpToDate]
    ON [Payments].[Settlement]([MerchantId] ASC, [UpToDate] ASC);
