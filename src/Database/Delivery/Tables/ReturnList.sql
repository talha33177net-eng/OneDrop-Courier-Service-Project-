-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.ReturnList
-- Purpose: Returning parcels the hub that collected them (HubId) sends back with a rider to one merchant's pickup point,
--          instead of handing them over at its counter; the parcels are Delivery.ReturnListParcel rows. Status (TINYINT
--          enum Domain.Delivery.ReturnListStatus): 1 Out with the rider, 2 HandedOver (HandedOverOn; the parcels are
--          returned and charged), 3 Confirmed by the merchant's login ConfirmedById at ConfirmedOn, its signature for
--          them, 4 NotHandedOver (the rider brings them back to the hub). Note is the rider's reason, or what the
--          merchant said when confirming. Number (RL-100001) comes from Delivery.ReturnListNumber.
-- Author: Courier team
-- Date: 2026-10-07
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[ReturnList] (
    [Id]            BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]      BIGINT         NOT NULL,
    [MerchantId]    BIGINT         NOT NULL,
    [PickupPointId] BIGINT         NOT NULL,
    [HubId]         BIGINT         NOT NULL,
    [RiderId]       BIGINT         NOT NULL,
    [Number]        NVARCHAR (20)  CONSTRAINT [DF_ReturnList_Number] DEFAULT (concat(N'RL-', NEXT VALUE FOR [Delivery].[ReturnListNumber])) NOT NULL,
    [Status]        TINYINT        NOT NULL,
    [HandedOverOn]  DATETIME2 (7)  NULL,
    [ConfirmedOn]   DATETIME2 (7)  NULL,
    [ConfirmedById] BIGINT         NULL,
    [Note]          NVARCHAR (300) NULL,
    [RowVersion]    ROWVERSION     NOT NULL,
    [UpdatedId]     BIGINT         NULL,
    [UpdatedOn]     DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]       DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ReturnList_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_ReturnList_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_ReturnList_PickupPoint] FOREIGN KEY ([PickupPointId]) REFERENCES [Merchants].[PickupPoint] ([Id]),
    CONSTRAINT [FK_ReturnList_Hub] FOREIGN KEY ([HubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_ReturnList_Rider] FOREIGN KEY ([RiderId]) REFERENCES [Delivery].[Rider] ([Id]),
    CONSTRAINT [FK_ReturnList_User_ConfirmedById] FOREIGN KEY ([ConfirmedById]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [FK_ReturnList_User_UpdatedId] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_ReturnList_Status] CHECK ([Status] BETWEEN 1 AND 4),
    CONSTRAINT [chk_ReturnList_HandedOver] CHECK (([Status] IN (1, 4) AND [HandedOverOn] IS NULL) OR ([Status] IN (2, 3) AND [HandedOverOn] IS NOT NULL)),
    CONSTRAINT [chk_ReturnList_Confirmed] CHECK (([Status] = 3 AND [ConfirmedOn] IS NOT NULL) OR ([Status] <> 3 AND [ConfirmedOn] IS NULL AND [ConfirmedById] IS NULL))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_ReturnList_Number]
    ON [Delivery].[ReturnList]([Number] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ReturnList_Tenant_Merchant_Status]
    ON [Delivery].[ReturnList]([TenantId] ASC, [MerchantId] ASC, [Status] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ReturnList_Tenant_Hub_Status]
    ON [Delivery].[ReturnList]([TenantId] ASC, [HubId] ASC, [Status] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_ReturnList_Rider_Status]
    ON [Delivery].[ReturnList]([RiderId] ASC, [Status] ASC);
