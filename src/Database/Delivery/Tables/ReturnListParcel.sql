-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.ReturnListParcel
-- Purpose: One parcel on a Delivery.ReturnList. MerchantId is copied from the parcel so the merchant filter needs no
--          join. A parcel can be on more than one list over time (a rider could not hand the first one over).
-- Author: Courier team
-- Date: 2026-10-07
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[ReturnListParcel] (
    [Id]           BIGINT        IDENTITY (1, 1) NOT NULL,
    [TenantId]     BIGINT        NOT NULL,
    [ReturnListId] BIGINT        NOT NULL,
    [ParcelId]     BIGINT        NOT NULL,
    [MerchantId]   BIGINT        NOT NULL,
    [UpdatedId]    BIGINT        NULL,
    [UpdatedOn]    DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL,
    [Created]      DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ReturnListParcel_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_ReturnListParcel_ReturnList] FOREIGN KEY ([ReturnListId]) REFERENCES [Delivery].[ReturnList] ([Id]),
    CONSTRAINT [FK_ReturnListParcel_Parcel] FOREIGN KEY ([ParcelId]) REFERENCES [Parcels].[Parcel] ([Id]),
    CONSTRAINT [FK_ReturnListParcel_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_ReturnListParcel_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_ReturnListParcel_ReturnList_Parcel]
    ON [Delivery].[ReturnListParcel]([ReturnListId] ASC, [ParcelId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ReturnListParcel_Tenant_Parcel]
    ON [Delivery].[ReturnListParcel]([TenantId] ASC, [ParcelId] ASC);
