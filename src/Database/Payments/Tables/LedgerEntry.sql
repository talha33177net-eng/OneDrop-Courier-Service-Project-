-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Payments.LedgerEntry
-- Purpose: What the operator owes each shop, line by line, one line of each kind per order (UX_LedgerEntry_Order_Kind).
--          Amount is positive when owed to the shop (Kind 1 Cod, from PaymentId) and negative when the shop owes it
--          (Kind 2 ReturnCharge, 3 LateHandoverFee). EntryDate is the tenant's day. SettlementId is set when a payout
--          takes the line; until then it counts towards the shop's next payout, so a charge is carried forward
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Payments].[LedgerEntry] (
    [Id]           BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]     BIGINT          NOT NULL,
    [MerchantId]   BIGINT          NOT NULL,
    [OrderId]      BIGINT          NOT NULL,
    [Kind]         TINYINT         NOT NULL,
    [Amount]       DECIMAL (12, 2) NOT NULL,
    [EntryDate]    DATE            NOT NULL,
    [PaymentId]    BIGINT          NULL,
    [SettlementId] BIGINT          NULL,
    [RowVersion]   ROWVERSION      NOT NULL,
    [UpdatedId]    BIGINT          NULL,
    [UpdatedOn]    DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]      DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_LedgerEntry_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_LedgerEntry_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_LedgerEntry_Order] FOREIGN KEY ([OrderId]) REFERENCES [Orders].[Order] ([Id]),
    CONSTRAINT [FK_LedgerEntry_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [Payments].[Payment] ([Id]),
    CONSTRAINT [FK_LedgerEntry_Settlement] FOREIGN KEY ([SettlementId]) REFERENCES [Payments].[Settlement] ([Id]),
    CONSTRAINT [FK_LedgerEntry_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_LedgerEntry_Kind] CHECK ([Kind] BETWEEN 1 AND 3),
    CONSTRAINT [chk_LedgerEntry_Amount] CHECK (([Kind] = 1 AND [Amount] > (0) AND [PaymentId] IS NOT NULL) OR ([Kind] > 1 AND [Amount] < (0)))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_LedgerEntry_Order_Kind]
    ON [Payments].[LedgerEntry]([OrderId] ASC, [Kind] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_Tenant_Merchant_Settlement]
    ON [Payments].[LedgerEntry]([TenantId] ASC, [MerchantId] ASC, [SettlementId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_Payment]
    ON [Payments].[LedgerEntry]([PaymentId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_Settlement]
    ON [Payments].[LedgerEntry]([SettlementId] ASC);
