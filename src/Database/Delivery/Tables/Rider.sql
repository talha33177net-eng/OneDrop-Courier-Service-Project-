-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.Rider
-- Purpose: A rider working from one hub, collecting from merchants and delivering to recipients. UserId is the rider's
--          login (unique while set). A rider who leaves is archived. The two keys to Identity.User are named
--          FK_Rider_User_UserId and FK_Rider_User_UpdatedId. Vehicle is what they ride, a TINYINT enum
--          (Domain.Delivery.Vehicle: 1 bicycle, 2 motorbike, 3 pickup van); Delivery.VehicleCapacity says how much it
--          carries
-- Author: Courier team
-- Date: 2026-10-04
--       2026-10-05 Vehicle (riders added before it were set to motorbike by Scripts/Pre/001_RiderVehicle.sql)
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[Rider] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]  BIGINT         NOT NULL,
    [HubId]     BIGINT         NOT NULL,
    [UserId]    BIGINT         NULL,
    [Name]      NVARCHAR (200) NOT NULL,
    [Phone]     NVARCHAR (20)  NOT NULL,
    [Vehicle]   TINYINT        NOT NULL,
    [Archived]  BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId] BIGINT         NULL,
    [UpdatedOn] DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]   DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Rider_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Rider_Hub] FOREIGN KEY ([HubId]) REFERENCES [Network].[Hub] ([Id]),
    CONSTRAINT [FK_Rider_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [FK_Rider_User_UpdatedId] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Rider_Vehicle] CHECK ([Vehicle] BETWEEN 1 AND 3)
);


GO
CREATE NONCLUSTERED INDEX [IX_Rider_Tenant_Hub]
    ON [Delivery].[Rider]([TenantId] ASC, [HubId] ASC);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Rider_User]
    ON [Delivery].[Rider]([UserId] ASC) WHERE ([UserId] IS NOT NULL);
