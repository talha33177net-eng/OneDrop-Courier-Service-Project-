-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Parcels.ParcelEvent
-- Purpose: A parcel's tracking history, one row per thing that happened: the status it was in (as Parcels.Parcel),
--          what happened and the hub it happened at. Created is when, UpdatedId who. MerchantId is copied from the
--          parcel so the merchant filter needs no join.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Parcels].[ParcelEvent] (
    [Id]         BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT         NOT NULL,
    [ParcelId]   BIGINT         NOT NULL,
    [MerchantId] BIGINT         NOT NULL,
    [Status]     TINYINT        NOT NULL,
    [Note]       NVARCHAR (300) NOT NULL,
    [HubId]      BIGINT         NULL,
    [UpdatedId]  BIGINT         NULL,
    [UpdatedOn]  DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ParcelEvent_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_ParcelEvent_Parcel] FOREIGN KEY ([ParcelId]) REFERENCES [Parcels].[Parcel] ([Id]),
    CONSTRAINT [FK_ParcelEvent_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_ParcelEvent_Hub] FOREIGN KEY ([HubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_ParcelEvent_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_ParcelEvent_Status] CHECK ([Status] BETWEEN 1 AND 11)
);


GO
CREATE NONCLUSTERED INDEX [IX_ParcelEvent_Tenant_Parcel]
    ON [Parcels].[ParcelEvent]([TenantId] ASC, [ParcelId] ASC);
