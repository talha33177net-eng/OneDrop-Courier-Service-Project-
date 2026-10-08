-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Merchants.MerchantPayoutAccount
-- Purpose: The bKash, Nagad or bank accounts a merchant account keeps for its payouts (Method: 1 bKash, 2 Nagad, 3 Bank;
--          Number is E.164 for a mobile wallet). It carries AccountId (the account's main profile) instead of the
--          usual MerchantId, because every business of the account shares them, and so it is not merchant-filtered.
--          The one payouts go to is copied onto the main profile's PayoutMethod, PayoutAccount and PayoutAccountName.
--          Removing one archives it; an account keeps a number once.
-- Author: Courier team
-- Date: 2026-10-07
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Merchants].[MerchantPayoutAccount] (
    [Id]        BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]  BIGINT         NOT NULL,
    [AccountId] BIGINT         NOT NULL,
    [Method]    TINYINT        NOT NULL,
    [Number]    NVARCHAR (30)  NOT NULL,
    [Name]      NVARCHAR (200) NOT NULL,
    [Archived]  BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId] BIGINT         NULL,
    [UpdatedOn] DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]   DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_MerchantPayoutAccount_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_MerchantPayoutAccount_Merchant] FOREIGN KEY ([AccountId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_MerchantPayoutAccount_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_MerchantPayoutAccount_Method] CHECK ([Method] BETWEEN 1 AND 3)
);


GO
-- An account keeps a number once; an archived one can be added again
CREATE UNIQUE NONCLUSTERED INDEX [UX_MerchantPayoutAccount_Account_Number]
    ON [Merchants].[MerchantPayoutAccount]([TenantId] ASC, [AccountId] ASC, [Method] ASC, [Number] ASC) WHERE ([Archived] = (0));
