-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Payments.OnlinePayment
-- Purpose: A merchant paying the courier what it owes, online through the payment gateway (SSLCommerz), with its
--          number (Number, PAY-100001). TransactionId is the random value the gateway knows it by (its tran_id).
--          Amount is the balance owed when it was started, in Currency. Status (TINYINT enum
--          Domain.Payments.OnlinePaymentStatus): 1 Started (sent to the payment page), 2 Paid (credited), 3 Failed
--          (cancelled or never finished; still credited if the gateway confirms it later), 4 Review (the gateway took the
--          money but marked it risky or confirmed another amount; credited only when the courier accepts it),
--          5 Refunded (held for review, then refunded through the gateway). PaidAmount, StoreAmount (after the gateway's
--          fee), Method, ValidationId and BankTransactionId are what the gateway said when asked by our server.
--          LedgerEntryId is the one credit it wrote on the merchant's balance (a Kind 5 adjustment line), so a payment
--          is credited once (UX_OnlinePayment_LedgerEntry)
-- Author: Courier team
-- Date: 2026-10-08
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Payments].[OnlinePayment] (
    [Id]                BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]          BIGINT          NOT NULL,
    [MerchantId]        BIGINT          NOT NULL,
    [Number]            NVARCHAR (20)   CONSTRAINT [DF_OnlinePayment_Number] DEFAULT (concat(N'PAY-', NEXT VALUE FOR [Payments].[OnlinePaymentNumber])) NOT NULL,
    [TransactionId]     NVARCHAR (30)   NOT NULL,
    [Amount]            DECIMAL (12, 2) NOT NULL,
    [Currency]          NVARCHAR (3)    NOT NULL,
    [Status]            TINYINT         NOT NULL,
    [PaidAmount]        DECIMAL (12, 2) NULL,
    [StoreAmount]       DECIMAL (12, 2) NULL,
    [Method]            NVARCHAR (50)   NULL,
    [ValidationId]      NVARCHAR (100)  NULL,
    [BankTransactionId] NVARCHAR (80)   NULL,
    [Note]              NVARCHAR (300)  NULL,
    [ConfirmedOn]       DATETIME2 (7)   NULL,
    [LedgerEntryId]     BIGINT          NULL,
    [RowVersion]        ROWVERSION      NOT NULL,
    [UpdatedId]         BIGINT          NULL,
    [UpdatedOn]         DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]           DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_OnlinePayment_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_OnlinePayment_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_OnlinePayment_LedgerEntry] FOREIGN KEY ([LedgerEntryId]) REFERENCES [Payments].[LedgerEntry] ([Id]),
    CONSTRAINT [FK_OnlinePayment_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_OnlinePayment_Amount] CHECK ([Amount] > (0)),
    CONSTRAINT [chk_OnlinePayment_Status] CHECK ([Status] BETWEEN 1 AND 5),
    CONSTRAINT [chk_OnlinePayment_Credit] CHECK (([Status] = 2 AND [LedgerEntryId] IS NOT NULL AND [ConfirmedOn] IS NOT NULL) OR ([Status] <> 2 AND [LedgerEntryId] IS NULL))
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_OnlinePayment_Number]
    ON [Payments].[OnlinePayment]([Number] ASC);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_OnlinePayment_TransactionId]
    ON [Payments].[OnlinePayment]([TransactionId] ASC);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_OnlinePayment_LedgerEntry]
    ON [Payments].[OnlinePayment]([LedgerEntryId] ASC) WHERE [LedgerEntryId] IS NOT NULL;


GO
CREATE NONCLUSTERED INDEX [IX_OnlinePayment_Tenant_Status]
    ON [Payments].[OnlinePayment]([TenantId] ASC, [Status] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_OnlinePayment_Merchant]
    ON [Payments].[OnlinePayment]([MerchantId] ASC, [Id] ASC);
