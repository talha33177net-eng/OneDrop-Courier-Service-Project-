-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 004_PayoutAccounts.sql
-- Purpose: A merchant account can keep several payout accounts (Merchants.MerchantPayoutAccount). The one each account
--          already pays to, held on its main profile, becomes its first saved account.
-- Author: Courier team
-- Date: 2026-10-07
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
INSERT INTO Merchants.MerchantPayoutAccount (TenantId, AccountId, Method, Number, [Name])
SELECT
    m.TenantId,
    m.Id,
    m.PayoutMethod,
    m.PayoutAccount,
    m.PayoutAccountName
FROM
    Merchants.Merchant m
WHERE
    m.MainMerchantId IS NULL AND
    m.PayoutMethod IS NOT NULL AND
    m.PayoutAccount IS NOT NULL AND
    m.PayoutAccountName IS NOT NULL;
