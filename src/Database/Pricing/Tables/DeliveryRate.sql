-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Pricing.DeliveryRate
-- Purpose: A tenant's rate card, one row per service area (TINYINT enum Domain.Pricing.ServiceArea: 1 inside city,
--          2 suburb, 3 outside city). The first IncludedWeightGrams cost BaseCharge, each started kg above it
--          ExtraKgCharge; CodChargePercent is taken off the cash collected, ReturnCharge added for a parcel that comes
--          back. A parcel keeps the charges it was booked at. No defaults: every tenant sets its own.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Pricing].[DeliveryRate] (
    [Id]                  BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]            BIGINT          NOT NULL,
    [ServiceArea]         TINYINT         NOT NULL,
    [IncludedWeightGrams] INT             NOT NULL,
    [BaseCharge]          DECIMAL (10, 2) NOT NULL,
    [ExtraKgCharge]       DECIMAL (10, 2) NOT NULL,
    [CodChargePercent]    DECIMAL (5, 2)  NOT NULL,
    [ReturnCharge]        DECIMAL (10, 2) NOT NULL,
    [UpdatedId]           BIGINT          NULL,
    [UpdatedOn]           DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]             DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DeliveryRate_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_DeliveryRate_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_DeliveryRate_ServiceArea] CHECK ([ServiceArea] BETWEEN 1 AND 3),
    CONSTRAINT [chk_DeliveryRate_Values] CHECK ([IncludedWeightGrams] > (0) AND [BaseCharge] >= (0) AND [ExtraKgCharge] >= (0) AND [CodChargePercent] BETWEEN 0 AND 10 AND [ReturnCharge] >= (0))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_DeliveryRate_Tenant_ServiceArea]
    ON [Pricing].[DeliveryRate]([TenantId] ASC, [ServiceArea] ASC);
