-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Platform.Tenant
-- Purpose: An operator (the Dhaka operator, a partner courier in another city). Every other business table carries a
--          TenantId pointing here. Prices, the grouping window and the SMS sender name are settings per tenant.
--          Selected per request by Slug, the subdomain: dhaka.onedrop.app
-- Author: Courier team
-- Date: 2026-09-27
-- 2026-09-28: No defaults for the settings: every tenant states its own prices, time zone and grouping window.
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Platform].[Tenant] (
    [Id]              BIGINT          IDENTITY (1, 1) NOT NULL,
    [Name]            NVARCHAR (200)  NOT NULL,
    [Slug]            NVARCHAR (50)   NOT NULL,
    [TimeZone]        NVARCHAR (100)  NOT NULL,
    [CurrencyCode]    NCHAR (3)       NOT NULL,
    [SmsSenderName]   NVARCHAR (20)   NOT NULL,
    -- Fee for the first shop in a delivery group, and for every extra distinct shop in it
    [BaseDeliveryFee] DECIMAL (10, 2) NOT NULL,
    [ExtraShopFee]    DECIMAL (10, 2) NOT NULL,
    [FastDeliveryFee] DECIMAL (10, 2) NOT NULL,
    -- Days, counted from the first order's day, on which later orders still join the group (2 = Day 1 and Day 2)
    [GroupJoinDays]   INT             NOT NULL,
    [Archived]        BIT             DEFAULT ((0)) NOT NULL,
    [UpdatedId]       BIGINT          NULL,
    [UpdatedOn]       DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]         DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Tenant_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Tenant_GroupJoinDays] CHECK ([GroupJoinDays] BETWEEN 1 AND 7)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Tenant_Slug]
    ON [Platform].[Tenant]([Slug] ASC);
