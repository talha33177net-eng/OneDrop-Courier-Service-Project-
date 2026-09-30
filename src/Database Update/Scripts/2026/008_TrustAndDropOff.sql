-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 008_TrustAndDropOff.sql
-- Purpose: Task 3.8 settings (values chosen by the owner 2026-09-30), the same at both launch operators: after a
--          refusal or no-show a customer pays the fee in advance until they have accepted 3 deliveries since; a shop
--          with 3 late handovers in the last 30 days brings its parcels to the hub itself
-- Author: Courier team
-- Date: 2026-09-30
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE
    T
SET
    TrustedAgainAfterDeliveries = S.TrustedAgainAfterDeliveries,
    DropOffAfterLateHandovers = S.DropOffAfterLateHandovers,
    LateHandoverWindowDays = S.LateHandoverWindowDays,
    UpdatedOn = GETUTCDATE()
FROM
    Platform.Tenant T
    INNER JOIN
        (VALUES
            (N'dhaka',      3, 3, 30),
            (N'chattogram', 3, 3, 30)
        ) S (Slug, TrustedAgainAfterDeliveries, DropOffAfterLateHandovers, LateHandoverWindowDays)
    ON S.Slug = T.Slug;
