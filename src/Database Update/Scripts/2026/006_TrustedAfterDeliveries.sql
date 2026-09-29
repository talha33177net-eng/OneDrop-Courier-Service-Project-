-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 006_TrustedAfterDeliveries.sql
-- Purpose: Accepted deliveries after which a customer never pays the delivery fee in advance, even after a refusal
--          or when a shop asks (task 3.6b; value chosen by the owner 2026-09-29): 10 for both launch tenants
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE
    T
SET
    TrustedAfterDeliveries = S.TrustedAfterDeliveries,
    UpdatedOn = GETUTCDATE()
FROM
    Platform.Tenant T
    INNER JOIN
        (VALUES
            (N'dhaka',      10),
            (N'chattogram', 10)
        ) S (Slug, TrustedAfterDeliveries)
    ON S.Slug = T.Slug;
