-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.Trip
-- Purpose: One rider's run from their hub on one delivery day (DeliveryDate, the tenant's date); its deliveries are
--          Delivery.TripStop rows. Status is a TINYINT enum (Domain.Delivery.TripStatus). UX_Trip_Rider_DeliveryDate
--          allows one trip per rider a day
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[Trip] (
    [Id]           BIGINT        IDENTITY (1, 1) NOT NULL,
    [TenantId]     BIGINT        NOT NULL,
    [RiderId]      BIGINT        NOT NULL,
    [HubId]        BIGINT        NOT NULL,
    [DeliveryDate] DATE          NOT NULL,
    [Status]       TINYINT       NOT NULL,
    [StartedOn]    DATETIME2 (7) NULL,
    [RowVersion]   ROWVERSION    NOT NULL,
    [UpdatedId]    BIGINT        NULL,
    [UpdatedOn]    DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL,
    [Created]      DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Trip_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Trip_Rider] FOREIGN KEY ([RiderId]) REFERENCES [Delivery].[Rider] ([Id]),
    CONSTRAINT [FK_Trip_Hub] FOREIGN KEY ([HubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_Trip_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Trip_Rider_DeliveryDate]
    ON [Delivery].[Trip]([RiderId] ASC, [DeliveryDate] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Trip_Tenant_Hub_DeliveryDate]
    ON [Delivery].[Trip]([TenantId] ASC, [HubId] ASC, [DeliveryDate] ASC);
