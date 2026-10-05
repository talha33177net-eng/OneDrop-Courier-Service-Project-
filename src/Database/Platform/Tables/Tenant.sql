-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Platform.Tenant
-- Purpose: A courier company on the platform. Every other business table carries a TenantId pointing here. Selected
--          per request by Slug, the subdomain: onedrop.onedrop.app. Its rate card is Pricing.DeliveryRate. No setting
--          has a default: every tenant states its own; a NULL setting means the tenant sets no such rule.
-- Author: Courier team
-- Date: 2026-10-04
--       2026-10-05 RiderReturnTime
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Platform].[Tenant] (
    [Id]                  BIGINT         IDENTITY (1, 1) NOT NULL,
    [Name]                NVARCHAR (200) NOT NULL,
    [Slug]                NVARCHAR (50)  NOT NULL,
    [TimeZone]            NVARCHAR (100) NOT NULL,
    [CurrencyCode]        NCHAR (3)      NOT NULL,
    [SmsSenderName]       NVARCHAR (20)  NOT NULL,
    [SupportPhone]        NVARCHAR (20)  NOT NULL,
    -- Delivery attempts a parcel gets before it must be delivered or returned
    [MaxDeliveryAttempts] INT            NOT NULL,
    -- The time of day (the tenant's clock) riders are due back at their hub with the day's cash; NULL for none
    [RiderReturnTime]     TIME (0)       NULL,
    [Archived]            BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId]           BIGINT         NULL,
    [UpdatedOn]           DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]             DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Tenant_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Tenant_MaxDeliveryAttempts] CHECK ([MaxDeliveryAttempts] BETWEEN 1 AND 10)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Tenant_Slug]
    ON [Platform].[Tenant]([Slug] ASC);
