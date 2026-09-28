-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.TripStop
-- Purpose: A delivery group on a trip. DeliveryDate is copied from the trip so UX_TripStop_DeliveryGroup_DeliveryDate
--          keeps a delivery on one trip a day, which settles two planners running at once. A delivery back at the
--          hub after a failed attempt gets a new stop on another day
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[TripStop] (
    [Id]              BIGINT        IDENTITY (1, 1) NOT NULL,
    [TenantId]        BIGINT        NOT NULL,
    [TripId]          BIGINT        NOT NULL,
    [DeliveryGroupId] BIGINT        NOT NULL,
    [DeliveryDate]    DATE          NOT NULL,
    [UpdatedId]       BIGINT        NULL,
    [UpdatedOn]       DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL,
    [Created]         DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_TripStop_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_TripStop_Trip] FOREIGN KEY ([TripId]) REFERENCES [Delivery].[Trip] ([Id]),
    CONSTRAINT [FK_TripStop_DeliveryGroup] FOREIGN KEY ([DeliveryGroupId]) REFERENCES [Grouping].[DeliveryGroup] ([Id]),
    CONSTRAINT [FK_TripStop_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_TripStop_DeliveryGroup_DeliveryDate]
    ON [Delivery].[TripStop]([DeliveryGroupId] ASC, [DeliveryDate] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TripStop_TenantId]
    ON [Delivery].[TripStop]([TenantId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TripStop_Trip]
    ON [Delivery].[TripStop]([TripId] ASC);
