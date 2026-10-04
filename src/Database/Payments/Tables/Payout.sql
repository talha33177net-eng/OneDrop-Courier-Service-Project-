-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Payments.Payout
-- Purpose: A payment to a merchant with its invoice number (Number, INV-100001): every ledger line of the merchant not
--          yet paid out up to UpToDate. Amount = CodTotal - ChargesTotal. Method (1 bKash, 2 Nagad, 3 Bank) and
--          Account are the merchant's payout account when it was made. Status (TINYINT enum
--          Domain.Payments.PayoutStatus): 1 Pending until the payout gateway confirms the transfer, 2 Paid.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Payments].[Payout] (
    [Id]               BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]         BIGINT          NOT NULL,
    [MerchantId]       BIGINT          NOT NULL,
    [Number]           NVARCHAR (20)   CONSTRAINT [DF_Payout_Number] DEFAULT (concat(N'INV-', NEXT VALUE FOR [Payments].[PayoutNumber])) NOT NULL,
    [UpToDate]         DATE            NOT NULL,
    [CodTotal]         DECIMAL (12, 2) NOT NULL,
    [ChargesTotal]     DECIMAL (12, 2) NOT NULL,
    [Amount]           DECIMAL (12, 2) NOT NULL,
    [Method]           TINYINT         NOT NULL,
    [Account]          NVARCHAR (30)   NOT NULL,
    [Status]           TINYINT         NOT NULL,
    [GatewayReference] NVARCHAR (100)  NULL,
    [PaidOn]           DATETIME2 (7)   NULL,
    [RowVersion]       ROWVERSION      NOT NULL,
    [UpdatedId]        BIGINT          NULL,
    [UpdatedOn]        DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]          DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Payout_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Payout_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_Payout_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Payout_Amount] CHECK ([Amount] > (0) AND [Amount] = [CodTotal] - [ChargesTotal]),
    CONSTRAINT [chk_Payout_Method] CHECK ([Method] BETWEEN 1 AND 3),
    CONSTRAINT [chk_Payout_Status] CHECK ([Status] BETWEEN 1 AND 2)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Payout_Number]
    ON [Payments].[Payout]([Number] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Payout_Tenant_Status]
    ON [Payments].[Payout]([TenantId] ASC, [Status] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Payout_Merchant_UpToDate]
    ON [Payments].[Payout]([MerchantId] ASC, [UpToDate] ASC);
