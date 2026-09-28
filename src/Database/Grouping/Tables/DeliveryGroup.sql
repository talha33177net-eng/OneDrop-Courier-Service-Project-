-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Grouping.DeliveryGroup
-- Purpose: All of one customer's orders to one address that travel together (Orders.Order.DeliveryGroupId).
--          Status is a TINYINT enum (Domain.Grouping.DeliveryGroupStatus). LocksAt is midnight at the start of
--          delivery day in the tenant's time zone, stored as UTC and fixed when the group opens: orders before
--          it join, orders after it open a new group. UX_DeliveryGroup_Customer_Address_Open allows one Open
--          group per customer and address, which settles the race when two orders arrive at the same moment.
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Grouping].[DeliveryGroup] (
    [Id]         BIGINT        IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT        NOT NULL,
    [CustomerId] BIGINT        NOT NULL,
    [AddressId]  BIGINT        NOT NULL,
    [HubId]      BIGINT        NOT NULL,
    [Number]     NVARCHAR (20) CONSTRAINT [DF_DeliveryGroup_Number] DEFAULT (concat(N'DG-', NEXT VALUE FOR [Grouping].[DeliveryGroupNumber])) NOT NULL,
    [Status]     TINYINT       NOT NULL,
    [OpenedOn]   DATETIME2 (7) NOT NULL,
    [LocksAt]    DATETIME2 (7) NOT NULL,
    [LockedOn]   DATETIME2 (7) NULL,
    [RowVersion] ROWVERSION    NOT NULL,
    [UpdatedId]  BIGINT        NULL,
    [UpdatedOn]  DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DeliveryGroup_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_DeliveryGroup_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [Customers].[Customer] ([Id]),
    CONSTRAINT [FK_DeliveryGroup_CustomerAddress] FOREIGN KEY ([AddressId]) REFERENCES [Customers].[CustomerAddress] ([Id]),
    CONSTRAINT [FK_DeliveryGroup_Hub] FOREIGN KEY ([HubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_DeliveryGroup_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_DeliveryGroup_LocksAt] CHECK ([LocksAt] > [OpenedOn])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_DeliveryGroup_Number]
    ON [Grouping].[DeliveryGroup]([Number] ASC);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_DeliveryGroup_Customer_Address_Open]
    ON [Grouping].[DeliveryGroup]([CustomerId] ASC, [AddressId] ASC) WHERE ([Status] = (1));


GO
CREATE NONCLUSTERED INDEX [IX_DeliveryGroup_Tenant_Status_LocksAt]
    ON [Grouping].[DeliveryGroup]([TenantId] ASC, [Status] ASC, [LocksAt] ASC);
