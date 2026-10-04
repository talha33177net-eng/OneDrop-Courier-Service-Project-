-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Payments.LedgerEntry
-- Purpose: What the courier owes each merchant, line by line, one line of each kind per parcel
--          (UX_LedgerEntry_Parcel_Kind). Amount is positive when owed to the merchant (Kind 1 Cod) and negative when the
--          merchant owes it (2 DeliveryCharge, 3 CodCharge, 4 ReturnCharge). EntryDate is the tenant's day. PayoutId is
--          set when a payout takes the line; until then it counts towards the merchant's next payout, so a charge the
--          day's cash does not cover is carried forward
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Payments].[LedgerEntry] (
    [Id]         BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT          NOT NULL,
    [MerchantId] BIGINT          NOT NULL,
    [ParcelId]   BIGINT          NOT NULL,
    [Kind]       TINYINT         NOT NULL,
    [Amount]     DECIMAL (12, 2) NOT NULL,
    [EntryDate]  DATE            NOT NULL,
    [PayoutId]   BIGINT          NULL,
    [RowVersion] ROWVERSION      NOT NULL,
    [UpdatedId]  BIGINT          NULL,
    [UpdatedOn]  DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_LedgerEntry_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_LedgerEntry_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_LedgerEntry_Parcel] FOREIGN KEY ([ParcelId]) REFERENCES [Parcels].[Parcel] ([Id]),
    CONSTRAINT [FK_LedgerEntry_Payout] FOREIGN KEY ([PayoutId]) REFERENCES [Payments].[Payout] ([Id]),
    CONSTRAINT [FK_LedgerEntry_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_LedgerEntry_Kind] CHECK ([Kind] BETWEEN 1 AND 4),
    CONSTRAINT [chk_LedgerEntry_Amount] CHECK (([Kind] = 1 AND [Amount] > (0)) OR ([Kind] > 1 AND [Amount] < (0)))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_LedgerEntry_Parcel_Kind]
    ON [Payments].[LedgerEntry]([ParcelId] ASC, [Kind] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_Tenant_Merchant_Payout]
    ON [Payments].[LedgerEntry]([TenantId] ASC, [MerchantId] ASC, [PayoutId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_Payout]
    ON [Payments].[LedgerEntry]([PayoutId] ASC);
