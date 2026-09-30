-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Notifications.OutboxMessage
-- Purpose: A domain event waiting to be acted on, written in the same transaction as the change that raised it.
--          Type names the message contract (Application.Notifications); Payload is its JSON (ids only). Status
--          is a TINYINT enum (Domain.Notifications.OutboxStatus): 1 Pending, 2 Sent, 3 Failed. A failed attempt
--          sets NextAttemptOn; NULL means send as soon as possible. Created is when the event happened.
-- Author: Courier team
-- Date: 2026-09-28
-- 2026-09-30: Status 4 Skipped (task 4.2): an order status change for a shop with no webhook
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Notifications].[OutboxMessage] (
    [Id]            BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]      BIGINT          NOT NULL,
    [Type]          NVARCHAR (100)  NOT NULL,
    [Payload]       NVARCHAR (1000) NOT NULL,
    [Status]        TINYINT         NOT NULL,
    [Attempts]      INT             NOT NULL,
    [NextAttemptOn] DATETIME2 (7)   NULL,
    [SentOn]        DATETIME2 (7)   NULL,
    [LastError]     NVARCHAR (1000) NULL,
    [UpdatedId]     BIGINT          NULL,
    [UpdatedOn]     DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]       DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_OutboxMessage_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_OutboxMessage_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_OutboxMessage_Tenant_Status_NextAttemptOn]
    ON [Notifications].[OutboxMessage]([TenantId] ASC, [Status] ASC, [NextAttemptOn] ASC);
