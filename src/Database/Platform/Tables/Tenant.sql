-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Platform.Tenant
-- Purpose: An operator (the Dhaka operator, a partner courier in another city). Every other business table carries a
--          TenantId pointing here. Prices, the grouping window and the SMS sender name are settings per tenant.
--          Selected per request by Slug, the subdomain: dhaka.onedrop.app
-- Author: Courier team
-- Date: 2026-09-27
-- 2026-09-28: No defaults for the settings: every tenant states its own prices, time zone and grouping window.
-- 2026-09-29: WeightAllowanceGrams and ExtraKgFee (task 3.4a). Nullable only because the launch seed
--             (DbUp 2026/001) inserts tenants without them and cannot be changed; DbUp 2026/004 sets them for the
--             launch tenants, and the application does not serve a tenant that has not set them.
-- 2026-09-29: TrustedAfterDeliveries (task 3.6b): accepted deliveries after which a customer never pays the fee in
--             advance. Nullable for the same reason; DbUp 2026/006 sets it for the launch tenants.
-- 2026-09-29: ReturnCharge and LateHandoverFee (task 3.7): what a shop pays for an order that comes back to it and for
--             one a rider had to leave behind, taken off its payout. Nullable for the same reason; DbUp 2026/007 sets
--             them for the launch tenants.
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Platform].[Tenant] (
    [Id]                     BIGINT          IDENTITY (1, 1) NOT NULL,
    [Name]                   NVARCHAR (200)  NOT NULL,
    [Slug]                   NVARCHAR (50)   NOT NULL,
    [TimeZone]               NVARCHAR (100)  NOT NULL,
    [CurrencyCode]           NCHAR (3)       NOT NULL,
    [SmsSenderName]          NVARCHAR (20)   NOT NULL,
    -- Fee for the first shop in a delivery group, and for every extra distinct shop in it
    [BaseDeliveryFee]        DECIMAL (10, 2) NOT NULL,
    [ExtraShopFee]           DECIMAL (10, 2) NOT NULL,
    [FastDeliveryFee]        DECIMAL (10, 2) NOT NULL,
    -- Days, counted from the first order's day, on which later orders still join the group (2 = Day 1 and Day 2)
    [GroupJoinDays]          INT             NOT NULL,
    -- Grams each shop's parcels may weigh in a delivery, and the fee for every started kg above that
    [WeightAllowanceGrams]   INT             NULL,
    [ExtraKgFee]             DECIMAL (10, 2) NULL,
    -- Accepted deliveries after which a customer never pays the delivery fee in advance
    [TrustedAfterDeliveries] INT             NULL,
    -- What a shop pays for an order that comes back to it, and for one a rider had to leave behind
    [ReturnCharge]           DECIMAL (10, 2) NULL,
    [LateHandoverFee]        DECIMAL (10, 2) NULL,
    [Archived]               BIT             DEFAULT ((0)) NOT NULL,
    [UpdatedId]              BIGINT          NULL,
    [UpdatedOn]              DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]                DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Tenant_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Tenant_GroupJoinDays] CHECK ([GroupJoinDays] BETWEEN 1 AND 7),
    CONSTRAINT [chk_Tenant_WeightAllowanceGrams] CHECK ([WeightAllowanceGrams] >= (0)),
    CONSTRAINT [chk_Tenant_ExtraKgFee] CHECK ([ExtraKgFee] >= (0)),
    CONSTRAINT [chk_Tenant_TrustedAfterDeliveries] CHECK ([TrustedAfterDeliveries] >= (1)),
    CONSTRAINT [chk_Tenant_ReturnCharge] CHECK ([ReturnCharge] >= (0)),
    CONSTRAINT [chk_Tenant_LateHandoverFee] CHECK ([LateHandoverFee] >= (0))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Tenant_Slug]
    ON [Platform].[Tenant]([Slug] ASC);
