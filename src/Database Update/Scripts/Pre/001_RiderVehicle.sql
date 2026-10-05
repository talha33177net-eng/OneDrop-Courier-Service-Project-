-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 001_RiderVehicle.sql
-- Purpose: Riders now say what they ride (Delivery.Rider.Vehicle, NOT NULL with no default), which SqlPackage cannot
--          add to a table with rows. Riders added before it are taken to ride a motorbike (2), the usual courier
--          vehicle in Bangladesh; the admin corrects them on the Riders page. Runs before the publish, against the old
--          schema; does nothing on a new database or once the column exists.
-- Author: Courier team
-- Date: 2026-10-05
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
IF OBJECT_ID(N'Delivery.Rider', N'U') IS NOT NULL AND
   COL_LENGTH(N'Delivery.Rider', N'Vehicle') IS NULL
BEGIN
    ALTER TABLE Delivery.Rider ADD [Vehicle] TINYINT NULL;

    -- The new column is not known when this batch is compiled, so the rest runs as its own batch
    EXEC (N'
        UPDATE Delivery.Rider
        SET [Vehicle] = 2;

        ALTER TABLE Delivery.Rider ALTER COLUMN [Vehicle] TINYINT NOT NULL;');
END;
