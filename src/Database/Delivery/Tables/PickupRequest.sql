-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.PickupRequest
-- Purpose: A merchant asking for a rider to collect its parcels at one of its pickup points on PickupDate (the
--          tenant's date). HubId is the hub of the point's zone, which assigns a rider. Status is a TINYINT enum
--          (Domain.Delivery.PickupStatus): 1 Requested, 2 Assigned, 3 Completed, 4 Cancelled.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[PickupRequest] (
    [Id]              BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]        BIGINT         NOT NULL,
    [MerchantId]      BIGINT         NOT NULL,
    [PickupPointId]   BIGINT         NOT NULL,
    [HubId]           BIGINT         NOT NULL,
    [PickupDate]      DATE           NOT NULL,
    [ExpectedParcels] INT            NOT NULL,
    [Note]            NVARCHAR (300) NULL,
    [Status]          TINYINT        NOT NULL,
    [RiderId]         BIGINT         NULL,
    [PickedParcels]   INT            NULL,
    [CompletedOn]     DATETIME2 (7)  NULL,
    [RowVersion]      ROWVERSION     NOT NULL,
    [UpdatedId]       BIGINT         NULL,
    [UpdatedOn]       DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]         DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_PickupRequest_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_PickupRequest_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_PickupRequest_PickupPoint] FOREIGN KEY ([PickupPointId]) REFERENCES [Merchants].[PickupPoint] ([Id]),
    CONSTRAINT [FK_PickupRequest_Hub] FOREIGN KEY ([HubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_PickupRequest_Rider] FOREIGN KEY ([RiderId]) REFERENCES [Delivery].[Rider] ([Id]),
    CONSTRAINT [FK_PickupRequest_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_PickupRequest_Status] CHECK ([Status] BETWEEN 1 AND 4),
    CONSTRAINT [chk_PickupRequest_Parcels] CHECK ([ExpectedParcels] > (0) AND ([PickedParcels] IS NULL OR [PickedParcels] >= (0)))
);


GO
CREATE NONCLUSTERED INDEX [IX_PickupRequest_Tenant_Hub_Status]
    ON [Delivery].[PickupRequest]([TenantId] ASC, [HubId] ASC, [Status] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PickupRequest_Tenant_Merchant]
    ON [Delivery].[PickupRequest]([TenantId] ASC, [MerchantId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_PickupRequest_Rider]
    ON [Delivery].[PickupRequest]([RiderId] ASC) WHERE ([RiderId] IS NOT NULL);
