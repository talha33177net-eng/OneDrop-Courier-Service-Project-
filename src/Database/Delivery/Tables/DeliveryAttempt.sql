-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.DeliveryAttempt
-- Purpose: One parcel on a rider's run: handed over at the hub (AssignedOn), then what happened at the door. Outcome is
--          a TINYINT enum (Domain.Delivery.AttemptOutcome): 1 Delivered, 2 PartlyDelivered, 3 Hold, 4 Refused; NULL
--          while the rider still has the parcel. CollectedAmount is the cash taken at the door. MerchantId is copied
--          from the parcel so the merchant filter needs no join.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[DeliveryAttempt] (
    [Id]              BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]        BIGINT          NOT NULL,
    [RunId]           BIGINT          NOT NULL,
    [RiderId]         BIGINT          NOT NULL,
    [ParcelId]        BIGINT          NOT NULL,
    [MerchantId]      BIGINT          NOT NULL,
    [AssignedOn]      DATETIME2 (7)   NOT NULL,
    [Outcome]         TINYINT         NULL,
    [CollectedAmount] DECIMAL (12, 2) DEFAULT ((0)) NOT NULL,
    [Reason]          NVARCHAR (200)  NULL,
    [CompletedOn]     DATETIME2 (7)   NULL,
    [UpdatedId]       BIGINT          NULL,
    [UpdatedOn]       DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]         DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DeliveryAttempt_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_DeliveryAttempt_DeliveryRun] FOREIGN KEY ([RunId]) REFERENCES [Delivery].[DeliveryRun] ([Id]),
    CONSTRAINT [FK_DeliveryAttempt_Rider] FOREIGN KEY ([RiderId]) REFERENCES [Delivery].[Rider] ([Id]),
    CONSTRAINT [FK_DeliveryAttempt_Parcel] FOREIGN KEY ([ParcelId]) REFERENCES [Parcels].[Parcel] ([Id]),
    CONSTRAINT [FK_DeliveryAttempt_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_DeliveryAttempt_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_DeliveryAttempt_Outcome] CHECK ([Outcome] BETWEEN 1 AND 4),
    CONSTRAINT [chk_DeliveryAttempt_Collected] CHECK ([CollectedAmount] >= (0) AND ([Outcome] IN (1, 2) OR [CollectedAmount] = (0)))
);


GO
CREATE NONCLUSTERED INDEX [IX_DeliveryAttempt_Tenant_Run]
    ON [Delivery].[DeliveryAttempt]([TenantId] ASC, [RunId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_DeliveryAttempt_Parcel]
    ON [Delivery].[DeliveryAttempt]([ParcelId] ASC);


GO
-- A parcel is with one rider at a time: one attempt open per parcel
CREATE UNIQUE NONCLUSTERED INDEX [UX_DeliveryAttempt_Parcel_Open]
    ON [Delivery].[DeliveryAttempt]([ParcelId] ASC) WHERE ([Outcome] IS NULL);
