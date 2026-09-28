-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Network.PickupRoute
-- Purpose: The daily pickup run of a zone: a collector visits the zone's merchant pickup points that have parcels
--          waiting and brings them to the hub. PickupTime is the departure in the tenant's time zone, a tenant
--          setting with no default. UX_PickupRoute_Zone_Active allows one active route per zone.
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Network].[PickupRoute] (
    [Id]         BIGINT        IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT        NOT NULL,
    [ZoneId]     BIGINT        NOT NULL,
    [PickupTime] TIME (0)      NOT NULL,
    [Archived]   BIT           DEFAULT ((0)) NOT NULL,
    [UpdatedId]  BIGINT        NULL,
    [UpdatedOn]  DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_PickupRoute_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_PickupRoute_Zone] FOREIGN KEY ([ZoneId]) REFERENCES [Network].[Zone] ([Id]),
    CONSTRAINT [FK_PickupRoute_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_PickupRoute_TenantId]
    ON [Network].[PickupRoute]([TenantId] ASC);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_PickupRoute_Zone_Active]
    ON [Network].[PickupRoute]([ZoneId] ASC) WHERE ([Archived] = (0));
