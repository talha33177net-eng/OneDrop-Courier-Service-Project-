-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Delivery.TripStop
-- Purpose: A delivery group on a trip. DeliveryDate is copied from the trip so UX_TripStop_DeliveryGroup_DeliveryDate
--          keeps a delivery on one trip a day, which settles two planners running at once. A delivery back at the
--          hub after a failed attempt gets a new stop on another day
-- Author: Courier team
-- Date: 2026-09-28
-- Updated: 2026-09-29 - Added Outcome (TINYINT enum Domain.Delivery.StopOutcome: 1 Delivered, 2 Refused, 3 NotHome),
--          FeeCollected, CodCollected and CompletedOn: what happened at the door and what the rider collected there
--          (task 3.5). A visit covering several deliveries of one customer splits its one fee over their stops
-- Updated: 2026-09-29 - Added PaymentId (Payments.Payment): what the customer paid the visit with (task 3.6a); every
--          stop of a visit points at the same payment, and a stop that collected money always has one
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Delivery].[TripStop] (
    [Id]              BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]        BIGINT          NOT NULL,
    [TripId]          BIGINT          NOT NULL,
    [DeliveryGroupId] BIGINT          NOT NULL,
    [DeliveryDate]    DATE            NOT NULL,
    [Outcome]         TINYINT         NULL,
    [FeeCollected]    DECIMAL (10, 2) NULL,
    [CodCollected]    DECIMAL (12, 2) NULL,
    [CompletedOn]     DATETIME2 (7)   NULL,
    [PaymentId]       BIGINT          NULL,
    [UpdatedId]       BIGINT          NULL,
    [UpdatedOn]       DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]         DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_TripStop_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_TripStop_Trip] FOREIGN KEY ([TripId]) REFERENCES [Delivery].[Trip] ([Id]),
    CONSTRAINT [FK_TripStop_DeliveryGroup] FOREIGN KEY ([DeliveryGroupId]) REFERENCES [Grouping].[DeliveryGroup] ([Id]),
    CONSTRAINT [FK_TripStop_Payment] FOREIGN KEY ([PaymentId]) REFERENCES [Payments].[Payment] ([Id]),
    CONSTRAINT [FK_TripStop_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_TripStop_Outcome] CHECK ([Outcome] BETWEEN 1 AND 3),
    CONSTRAINT [chk_TripStop_Collected] CHECK ([FeeCollected] >= (0) AND [CodCollected] >= (0))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_TripStop_DeliveryGroup_DeliveryDate]
    ON [Delivery].[TripStop]([DeliveryGroupId] ASC, [DeliveryDate] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TripStop_TenantId]
    ON [Delivery].[TripStop]([TenantId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TripStop_Trip]
    ON [Delivery].[TripStop]([TripId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_TripStop_Payment]
    ON [Delivery].[TripStop]([PaymentId] ASC);
