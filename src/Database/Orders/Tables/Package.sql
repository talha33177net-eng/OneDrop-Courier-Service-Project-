-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Orders.Package
-- Purpose: One physical parcel of an order; its label reads {Order.Number}-{Sequence}. MerchantId is copied from
--          the order so the merchant filter needs no join.
-- Author: Courier team
-- Date: 2026-09-27
-- Updated: 2026-09-28 - Added HubId and ReceivedOn: the hub the parcel was last scanned in at, and when (task 3.2)
-- Updated: 2026-09-28 - Added ShuttleToHubId: the hub the parcel travels to on the hub shuttle; HubId is NULL on the
--          way (task 3.3). The two hub keys are named FK_Package_Hub_HubId and FK_Package_Hub_ShuttleToHubId
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Orders].[Package] (
    [Id]             BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]       BIGINT         NOT NULL,
    [OrderId]        BIGINT         NOT NULL,
    [MerchantId]     BIGINT         NOT NULL,
    [Sequence]       INT            NOT NULL,
    [Description]    NVARCHAR (200) NOT NULL,
    [WeightGrams]    INT            NOT NULL,
    [HubId]          BIGINT         NULL,
    [ReceivedOn]     DATETIME2 (7)  NULL,
    [ShuttleToHubId] BIGINT         NULL,
    [UpdatedId]      BIGINT         NULL,
    [UpdatedOn]      DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]        DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Package_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Package_Order] FOREIGN KEY ([OrderId]) REFERENCES [Orders].[Order] ([Id]),
    CONSTRAINT [FK_Package_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_Package_Hub_HubId] FOREIGN KEY ([HubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_Package_Hub_ShuttleToHubId] FOREIGN KEY ([ShuttleToHubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_Package_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Package_Weight] CHECK ([WeightGrams] > (0))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Package_Order_Sequence]
    ON [Orders].[Package]([OrderId] ASC, [Sequence] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Package_TenantId]
    ON [Orders].[Package]([TenantId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Package_Hub]
    ON [Orders].[Package]([HubId] ASC) WHERE ([HubId] IS NOT NULL);


GO
CREATE NONCLUSTERED INDEX [IX_Package_ShuttleToHub]
    ON [Orders].[Package]([ShuttleToHubId] ASC) WHERE ([ShuttleToHubId] IS NOT NULL);
