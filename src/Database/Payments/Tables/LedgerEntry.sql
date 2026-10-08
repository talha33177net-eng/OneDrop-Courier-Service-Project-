-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Payments.LedgerEntry
-- Purpose: What the courier owes each merchant, line by line, one line of each kind per parcel
--          (UX_LedgerEntry_Parcel_Kind). Amount is positive when owed to the merchant (Kind 1 Cod) and negative when the
--          merchant owes it (2 DeliveryCharge, 3 CodCharge, 4 ReturnCharge). EntryDate is the tenant's day. PayoutId is
--          set when a payout takes the line; until then it counts towards the merchant's next payout, so a charge the
--          day's cash does not cover is carried forward. Kind 5 Adjustment is written by the courier by hand, with no
--          parcel and a Note saying why: positive credits the merchant, negative charges it
-- 2026-10-07: Kind 5 Adjustment, Note; ParcelId NULL for an adjustment.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Payments].[LedgerEntry] (
    [Id]         BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT          NOT NULL,
    [MerchantId] BIGINT          NOT NULL,
    [ParcelId]   BIGINT          NULL,
    [Kind]       TINYINT         NOT NULL,
    [Amount]     DECIMAL (12, 2) NOT NULL,
    [EntryDate]  DATE            NOT NULL,
    [Note]       NVARCHAR (200)  NULL,
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
    CONSTRAINT [chk_LedgerEntry_Kind] CHECK ([Kind] BETWEEN 1 AND 5),
    CONSTRAINT [chk_LedgerEntry_Amount] CHECK (([Kind] = 1 AND [Amount] > (0)) OR ([Kind] BETWEEN 2 AND 4 AND [Amount] < (0)) OR ([Kind] = 5 AND [Amount] <> (0))),
    CONSTRAINT [chk_LedgerEntry_Parcel] CHECK (([Kind] < 5 AND [ParcelId] IS NOT NULL AND [Note] IS NULL) OR ([Kind] = 5 AND [ParcelId] IS NULL AND [Note] IS NOT NULL))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_LedgerEntry_Parcel_Kind]
    ON [Payments].[LedgerEntry]([ParcelId] ASC, [Kind] ASC) WHERE [ParcelId] IS NOT NULL;


GO
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_Tenant_Merchant_Payout]
    ON [Payments].[LedgerEntry]([TenantId] ASC, [MerchantId] ASC, [PayoutId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_LedgerEntry_Payout]
    ON [Payments].[LedgerEntry]([PayoutId] ASC);
