-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Customers.CustomerAddress
-- Purpose: A delivery address of one customer. A delivery group is phone + address, so home and office orders
--          travel separately. MatchKey is Line1 + Line2 normalised in C# (CustomerAddress.BuildMatchKey) so two
--          spellings of one address land on one row; the unique index settles the race when two orders for a
--          new address arrive at once.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Customers].[CustomerAddress] (
    [Id]         BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT         NOT NULL,
    [CustomerId] BIGINT         NOT NULL,
    [AreaId]     BIGINT         NOT NULL,
    [Line1]      NVARCHAR (300) NOT NULL,
    [Line2]      NVARCHAR (300) NULL,
    [Landmark]   NVARCHAR (300) NULL,
    [MatchKey]   NVARCHAR (400) NOT NULL,
    [Archived]   BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId]  BIGINT         NULL,
    [UpdatedOn]  DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_CustomerAddress_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_CustomerAddress_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [Customers].[Customer] ([Id]),
    CONSTRAINT [FK_CustomerAddress_Area] FOREIGN KEY ([AreaId]) REFERENCES [Network].[Area] ([Id]),
    CONSTRAINT [FK_CustomerAddress_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_CustomerAddress_Customer_Area_MatchKey]
    ON [Customers].[CustomerAddress]([CustomerId] ASC, [AreaId] ASC, [MatchKey] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerAddress_TenantId]
    ON [Customers].[CustomerAddress]([TenantId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_CustomerAddress_AreaId]
    ON [Customers].[CustomerAddress]([AreaId] ASC);
