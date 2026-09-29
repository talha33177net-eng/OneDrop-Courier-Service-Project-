-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: Pre/003_DeliveryGroupKind.sql
-- Purpose: Grouping.DeliveryGroup.Kind is NOT NULL with no default (every group is opened as one kind on purpose),
--          and SqlPackage cannot add such a column to a table with rows. This script adds it nullable, fills it and
--          makes it NOT NULL; the publish that follows adds its check as the table file says.
--          A group holding a Deliver fast (Speed 2) or Don't hold order was opened alone: NextDay (2). Every other
--          group is Waiting (1), including the few sent early by Ship now before 3.4a: they were not charged the
--          fast difference, and ShippedNow (3) would charge it now.
--          Guarded and in dynamic SQL, so it does nothing on a new database or one that already has the column.
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

SET NOCOUNT ON;

IF OBJECT_ID(N'Grouping.DeliveryGroup', N'U') IS NOT NULL AND
    COL_LENGTH(N'Grouping.DeliveryGroup', N'Kind') IS NULL
BEGIN
    EXEC sp_executesql N'ALTER TABLE Grouping.DeliveryGroup ADD Kind TINYINT NULL;';

    EXEC sp_executesql N'
    UPDATE
        G
    SET
        Kind =
            CASE
                WHEN EXISTS (
                    SELECT
                        1
                    FROM
                        Orders.[Order] O
                    WHERE
                        O.DeliveryGroupId = G.Id AND
                        (O.Speed = 2 OR O.DoNotHold = 1)
                ) THEN 2
                ELSE 1
            END
    FROM
        Grouping.DeliveryGroup G;

    ALTER TABLE Grouping.DeliveryGroup ALTER COLUMN Kind TINYINT NOT NULL;';
END;
