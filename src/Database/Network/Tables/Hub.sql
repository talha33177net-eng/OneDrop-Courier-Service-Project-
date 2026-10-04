-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Network.Hub
-- Purpose: A branch where parcels are received, sorted, sent on to other hubs and handed to riders. A hub serves one
--          or more zones (Network.Zone.HubId): it collects from the pickup points and delivers to the areas there.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Network].[Hub] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]  BIGINT         NOT NULL,
    [Code]      NVARCHAR (20)  NOT NULL,
    [Name]      NVARCHAR (200) NOT NULL,
    [Address]   NVARCHAR (500) NOT NULL,
    [Phone]     NVARCHAR (20)  NOT NULL,
    [Archived]  BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId] BIGINT         NULL,
    [UpdatedOn] DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]   DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Hub_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Hub_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Hub_Tenant_Code]
    ON [Network].[Hub]([TenantId] ASC, [Code] ASC);
