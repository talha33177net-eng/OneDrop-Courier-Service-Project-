-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Network.Area
-- Purpose: The neighbourhood list addresses pick from ("Mirpur 10", "Banani DOHS"). Dhaka addresses are too messy
--          to geocode in the MVP, so every address names an area and the area decides the zone and hub.
--          Merchants read the list from GET /api/v1/areas.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Network].[Area] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]  BIGINT         NOT NULL,
    [ZoneId]    BIGINT         NOT NULL,
    [Name]      NVARCHAR (200) NOT NULL,
    [Archived]  BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId] BIGINT         NULL,
    [UpdatedOn] DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]   DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Area_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Area_Zone] FOREIGN KEY ([ZoneId]) REFERENCES [Network].[Zone] ([Id]),
    CONSTRAINT [FK_Area_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Area_Tenant_Name]
    ON [Network].[Area]([TenantId] ASC, [Name] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Area_ZoneId]
    ON [Network].[Area]([ZoneId] ASC);
