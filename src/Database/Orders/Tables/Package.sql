-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Orders.Package
-- Purpose: One physical parcel of an order; its label reads {Order.Number}-{Sequence}. MerchantId is copied from
--          the order so the merchant filter needs no join.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Orders].[Package] (
    [Id]          BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]    BIGINT         NOT NULL,
    [OrderId]     BIGINT         NOT NULL,
    [MerchantId]  BIGINT         NOT NULL,
    [Sequence]    INT            NOT NULL,
    [Description] NVARCHAR (200) NOT NULL,
    [WeightGrams] INT            NOT NULL,
    [UpdatedId]   BIGINT         NULL,
    [UpdatedOn]   DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]     DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Package_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Package_Order] FOREIGN KEY ([OrderId]) REFERENCES [Orders].[Order] ([Id]),
    CONSTRAINT [FK_Package_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_Package_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Package_Weight] CHECK ([WeightGrams] > (0))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Package_Order_Sequence]
    ON [Orders].[Package]([OrderId] ASC, [Sequence] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Package_TenantId]
    ON [Orders].[Package]([TenantId] ASC);
