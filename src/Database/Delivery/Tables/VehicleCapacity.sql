-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.VehicleCapacity
-- Purpose: What one kind of vehicle carries at once, in the tenant's own numbers: how many parcels, how much weight in
--          all and how heavy one parcel may be. Vehicle is a TINYINT enum (Domain.Delivery.Vehicle): 1 bicycle,
--          2 motorbike, 3 pickup van. A rider is never handed more than their vehicle carries, and a pickup goes to a
--          vehicle big enough for it. A vehicle with no row has no limit. No defaults: every tenant sets its own
-- Author: Courier team
-- Date: 2026-10-05
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[VehicleCapacity] (
    [Id]             BIGINT        IDENTITY (1, 1) NOT NULL,
    [TenantId]       BIGINT        NOT NULL,
    [Vehicle]        TINYINT       NOT NULL,
    [MaxParcels]     INT           NOT NULL,
    [MaxLoadGrams]   INT           NOT NULL,
    [MaxParcelGrams] INT           NOT NULL,
    [UpdatedId]      BIGINT        NULL,
    [UpdatedOn]      DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL,
    [Created]        DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_VehicleCapacity_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_VehicleCapacity_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_VehicleCapacity_Vehicle] CHECK ([Vehicle] BETWEEN 1 AND 3),
    CONSTRAINT [chk_VehicleCapacity_Values] CHECK ([MaxParcels] > (0) AND [MaxLoadGrams] > (0) AND [MaxParcelGrams] > (0) AND [MaxParcelGrams] <= [MaxLoadGrams])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_VehicleCapacity_Tenant_Vehicle]
    ON [Delivery].[VehicleCapacity]([TenantId] ASC, [Vehicle] ASC);
