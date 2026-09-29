-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 007_MerchantCharges.sql
-- Purpose: What a shop pays, taken off its payout (task 3.7; values chosen by the owner 2026-09-29): the return charge
--          for an order that comes back to it (Dhaka 30, Chattogram 35) and the late-handover fee for an order a rider
--          had to leave behind (Dhaka 25, Chattogram 30, the extra-shop fee the customer pays for the second trip)
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE
    T
SET
    ReturnCharge = S.ReturnCharge,
    LateHandoverFee = S.LateHandoverFee,
    UpdatedOn = GETUTCDATE()
FROM
    Platform.Tenant T
    INNER JOIN
        (VALUES
            (N'dhaka',      30, 25),
            (N'chattogram', 35, 30)
        ) S (Slug, ReturnCharge, LateHandoverFee)
    ON S.Slug = T.Slug;
