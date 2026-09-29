-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Orders.Order
-- Purpose: One merchant's order for one customer address. Status and Speed are TINYINT enums
--          (Domain.Orders.OrderStatus / DeliverySpeed). A merchant's retry with the same Idempotency-Key
--          returns the first order; RequestHash detects the key being reused for a different body.
--          RowVersion guards concurrent status changes (hub scan versus rider app).
-- Author: Courier team
-- Date: 2026-09-27
-- Updated: 2026-09-28 - Added DeliveryGroupId (Grouping.DeliveryGroup), NOT NULL: every order travels in a group
--          (Pre/001_GroupExistingOrders put the orders saved before grouping into groups)
-- Updated: 2026-09-28 - Added AddedFee: what the order added to its group's delivery fee when accepted (the base
--          fee, the extra-shop fee or 0), shown to the merchant; filled for older orders by
--          Pre/002_PriceExistingOrders
-- Updated: 2026-09-29 - Added LeftBehindOn: when a rider left without the order because it was not ready, and it
--          moved to a later delivery (task 3.5); the shop's late-handover fee (3.7) reads it
-- Updated: 2026-09-29 - Added CustomerStep (TINYINT enum Domain.Customers.CustomerStep: 0 None, 1 Confirm, 2
--          PayInAdvance), ConfirmedOn and CustomerToken (task 3.6b): what the order waits for from the customer, when
--          they confirmed or paid in advance, and the secret in their SMS link. An order waiting for the advance is not
--          collected from the shop
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Orders].[Order] (
    [Id]                BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]          BIGINT          NOT NULL,
    [MerchantId]        BIGINT          NOT NULL,
    [CustomerId]        BIGINT          NOT NULL,
    [AddressId]         BIGINT          NOT NULL,
    [DeliveryGroupId]   BIGINT          NOT NULL,
    [PickupPointId]     BIGINT          NOT NULL,
    [Number]            NVARCHAR (20)   CONSTRAINT [DF_Order_Number] DEFAULT (concat(N'OD-', NEXT VALUE FOR [Orders].[OrderNumber])) NOT NULL,
    [ExternalReference] NVARCHAR (100)  NULL,
    [IdempotencyKey]    NVARCHAR (100)  NULL,
    [RequestHash]       BINARY (32)     NULL,
    [RecipientName]     NVARCHAR (200)  NOT NULL,
    [Status]            TINYINT         NOT NULL,
    [Speed]             TINYINT         NOT NULL,
    [DoNotHold]         BIT             DEFAULT ((0)) NOT NULL,
    [CodAmount]         DECIMAL (12, 2) DEFAULT ((0)) NOT NULL,
    [DeclaredValue]     DECIMAL (12, 2) DEFAULT ((0)) NOT NULL,
    [AddedFee]          DECIMAL (10, 2) NOT NULL,
    [Note]              NVARCHAR (500)  NULL,
    [LeftBehindOn]      DATETIME2 (7)   NULL,
    [CustomerStep]      TINYINT         DEFAULT ((0)) NOT NULL,
    [ConfirmedOn]       DATETIME2 (7)   NULL,
    [CustomerToken]     NVARCHAR (30)   NULL,
    [RowVersion]        ROWVERSION      NOT NULL,
    [UpdatedId]         BIGINT          NULL,
    [UpdatedOn]         DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]           DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Order_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Order_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [Customers].[Customer] ([Id]),
    CONSTRAINT [FK_Order_CustomerAddress] FOREIGN KEY ([AddressId]) REFERENCES [Customers].[CustomerAddress] ([Id]),
    CONSTRAINT [FK_Order_DeliveryGroup] FOREIGN KEY ([DeliveryGroupId]) REFERENCES [Grouping].[DeliveryGroup] ([Id]),
    CONSTRAINT [FK_Order_PickupPoint] FOREIGN KEY ([PickupPointId]) REFERENCES [Merchants].[PickupPoint] ([Id]),
    CONSTRAINT [FK_Order_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Order_Amounts] CHECK ([CodAmount] >= (0) AND [DeclaredValue] >= (0) AND [AddedFee] >= (0)),
    CONSTRAINT [chk_Order_CustomerStep] CHECK ([CustomerStep] BETWEEN 0 AND 2)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Order_Number]
    ON [Orders].[Order]([Number] ASC);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Order_Merchant_IdempotencyKey]
    ON [Orders].[Order]([MerchantId] ASC, [IdempotencyKey] ASC) WHERE ([IdempotencyKey] IS NOT NULL);


GO
CREATE NONCLUSTERED INDEX [IX_Order_Tenant_Merchant_Created]
    ON [Orders].[Order]([TenantId] ASC, [MerchantId] ASC, [Created] DESC);


GO
CREATE NONCLUSTERED INDEX [IX_Order_Tenant_Customer_Status]
    ON [Orders].[Order]([TenantId] ASC, [CustomerId] ASC, [Status] ASC)
    INCLUDE([AddressId]);


GO
CREATE NONCLUSTERED INDEX [IX_Order_DeliveryGroupId]
    ON [Orders].[Order]([DeliveryGroupId] ASC);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Order_CustomerToken]
    ON [Orders].[Order]([CustomerToken] ASC) WHERE ([CustomerToken] IS NOT NULL);
