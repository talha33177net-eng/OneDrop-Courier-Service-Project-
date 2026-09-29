-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 005_StaggeredPickupTimes.sql
-- Purpose: The launch zones' pickup routes, staggered from 11:00 to 13:30 with the farthest zones first instead of all
--          at 2 PM (market review, 2026-09-28; times confirmed by the owner 2026-09-29). Every parcel is at its hub
--          by about 14:00, the hub shuttle leaves about 14:30 and riders go out at 17:00, so an order placed by the
--          end of Day 2, or a fast order from another zone, still makes its delivery day's trip.
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE
    R
SET
    PickupTime = S.PickupTime,
    UpdatedOn = GETUTCDATE()
FROM
    Network.PickupRoute R
    INNER JOIN
        Network.Zone Z
    ON Z.Id = R.ZoneId
    INNER JOIN
        Platform.Tenant T
    ON T.Id = Z.TenantId
    INNER JOIN
        (VALUES
            (N'dhaka',      N'UTT', CAST('11:00' AS TIME (0))),
            (N'dhaka',      N'MIR', CAST('11:30' AS TIME (0))),
            (N'dhaka',      N'MOT', CAST('12:00' AS TIME (0))),
            (N'dhaka',      N'MOH', CAST('12:30' AS TIME (0))),
            (N'dhaka',      N'DHN', CAST('13:00' AS TIME (0))),
            (N'dhaka',      N'BAN', CAST('13:00' AS TIME (0))),
            (N'dhaka',      N'GUL', CAST('13:30' AS TIME (0))),
            (N'chattogram', N'HAL', CAST('11:00' AS TIME (0))),
            (N'chattogram', N'NAS', CAST('11:30' AS TIME (0))),
            (N'chattogram', N'CHK', CAST('12:00' AS TIME (0))),
            (N'chattogram', N'AGR', CAST('12:30' AS TIME (0))),
            (N'chattogram', N'PAN', CAST('13:00' AS TIME (0)))
        ) S (Slug, ZoneCode, PickupTime)
    ON S.Slug = T.Slug AND
       S.ZoneCode = Z.Code
WHERE
    R.Archived = 0;
