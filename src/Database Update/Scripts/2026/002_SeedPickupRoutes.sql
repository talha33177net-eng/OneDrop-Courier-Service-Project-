-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 002_SeedPickupRoutes.sql
-- Purpose: One daily pickup route per zone of the two launch tenants, leaving at 2 PM local time as in the project
--          documentation ("2 PM Mirpur"). Each operator can move its own times later.
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
INSERT INTO Network.PickupRoute
    (TenantId, ZoneId, PickupTime)
SELECT
    Z.TenantId,
    Z.Id,
    '14:00'
FROM
    Network.Zone Z
    INNER JOIN
        Platform.Tenant T
    ON T.Id = Z.TenantId
WHERE
    T.Slug IN (N'dhaka', N'chattogram') AND
    Z.Archived = 0;
