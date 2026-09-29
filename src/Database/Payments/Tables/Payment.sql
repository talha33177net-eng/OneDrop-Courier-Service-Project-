-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Payments.Payment
-- Purpose: Money a customer paid. At the door: the visit's delivery fee (Fee) and the shops' cash on delivery (Cod)
--          together, kept apart for the ledger; TripId and RiderId say who collected it and DeliveryGroupId is the
--          visit's first delivery (the stops it paid for point here through Delivery.TripStop.PaymentId).
--          TINYINT enums (Domain.Payments): Purpose 1 Door; Method 1 Cash, 2 Bkash, 3 Nagad; Status 1 Pending,
--          2 Paid, 3 Cancelled. A QR payment is Pending with the gateway's reference and link until paid; cash is
--          Paid when recorded
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Payments].[Payment] (
    [Id]               BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]         BIGINT          NOT NULL,
    [CustomerId]       BIGINT          NOT NULL,
    [TripId]           BIGINT          NULL,
    [RiderId]          BIGINT          NULL,
    [DeliveryGroupId]  BIGINT          NOT NULL,
    [Purpose]          TINYINT         NOT NULL,
    [Method]           TINYINT         NOT NULL,
    [Status]           TINYINT         NOT NULL,
    [Fee]              DECIMAL (10, 2) NOT NULL,
    [Cod]              DECIMAL (12, 2) NOT NULL,
    [GatewayReference] NVARCHAR (100)  NULL,
    [PaymentLink]      NVARCHAR (500)  NULL,
    [PaidOn]           DATETIME2 (7)   NULL,
    [RowVersion]       ROWVERSION      NOT NULL,
    [UpdatedId]        BIGINT          NULL,
    [UpdatedOn]        DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]          DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Payment_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Payment_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [Customers].[Customer] ([Id]),
    CONSTRAINT [FK_Payment_Trip] FOREIGN KEY ([TripId]) REFERENCES [Delivery].[Trip] ([Id]),
    CONSTRAINT [FK_Payment_Rider] FOREIGN KEY ([RiderId]) REFERENCES [Delivery].[Rider] ([Id]),
    CONSTRAINT [FK_Payment_DeliveryGroup] FOREIGN KEY ([DeliveryGroupId]) REFERENCES [Grouping].[DeliveryGroup] ([Id]),
    CONSTRAINT [FK_Payment_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_Payment_Purpose] CHECK ([Purpose] = 1),
    CONSTRAINT [chk_Payment_Method] CHECK ([Method] BETWEEN 1 AND 3),
    CONSTRAINT [chk_Payment_Status] CHECK ([Status] BETWEEN 1 AND 3),
    CONSTRAINT [chk_Payment_Amounts] CHECK ([Fee] >= (0) AND [Cod] >= (0) AND [Fee] + [Cod] > (0))
);


GO
CREATE NONCLUSTERED INDEX [IX_Payment_Tenant_Customer]
    ON [Payments].[Payment]([TenantId] ASC, [CustomerId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Payment_Trip_DeliveryGroup]
    ON [Payments].[Payment]([TripId] ASC, [DeliveryGroupId] ASC);
