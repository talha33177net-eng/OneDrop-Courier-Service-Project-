-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Orders.OrderStatusHistory
-- Purpose: Append-only log of every status an order has had. Created is when it happened and UpdatedId who did
--          it (NULL for API keys and jobs). Rows are never updated.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Orders].[OrderStatusHistory] (
    [Id]         BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT         NOT NULL,
    [OrderId]    BIGINT         NOT NULL,
    [MerchantId] BIGINT         NOT NULL,
    [Status]     TINYINT        NOT NULL,
    [Note]       NVARCHAR (500) NULL,
    [UpdatedId]  BIGINT         NULL,
    [UpdatedOn]  DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_OrderStatusHistory_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_OrderStatusHistory_Order] FOREIGN KEY ([OrderId]) REFERENCES [Orders].[Order] ([Id]),
    CONSTRAINT [FK_OrderStatusHistory_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_OrderStatusHistory_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_OrderStatusHistory_OrderId]
    ON [Orders].[OrderStatusHistory]([OrderId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_OrderStatusHistory_TenantId]
    ON [Orders].[OrderStatusHistory]([TenantId] ASC);
