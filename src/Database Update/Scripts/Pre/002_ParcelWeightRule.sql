-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 002_ParcelWeightRule.sql
-- Purpose: A hub can now weigh a parcel and the charge follows its scale, so each parcel keeps the weight rule it was
--          booked under (Parcels.Parcel.IncludedWeightGrams, BaseCharge and ExtraKgCharge, NOT NULL with no default),
--          which SqlPackage cannot add to a table with rows. Parcels booked before it take the rule from their own
--          courier's rate card for the service area they were priced in, which is the rule they were booked under
--          unless that courier has changed its rates since. Runs before the publish, against the old schema; does
--          nothing on a new database or once the columns exist.
-- Author: Courier team
-- Date: 2026-10-05
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
IF OBJECT_ID(N'Parcels.Parcel', N'U') IS NOT NULL AND
   COL_LENGTH(N'Parcels.Parcel', N'IncludedWeightGrams') IS NULL
BEGIN
    ALTER TABLE Parcels.Parcel ADD
        [MeasuredWeightGrams] INT             NULL,
        [IncludedWeightGrams] INT             NULL,
        [BaseCharge]          DECIMAL (10, 2) NULL,
        [ExtraKgCharge]       DECIMAL (10, 2) NULL;

    -- The new columns are not known when this batch is compiled, so the rest runs as its own batch
    EXEC (N'
        UPDATE
            p
        SET
            p.IncludedWeightGrams = r.IncludedWeightGrams,
            p.BaseCharge = r.BaseCharge,
            p.ExtraKgCharge = r.ExtraKgCharge
        FROM
            Parcels.Parcel p
            INNER JOIN Pricing.DeliveryRate r
                ON r.TenantId = p.TenantId AND
                   r.ServiceArea = p.ServiceArea;

        -- A parcel whose courier has no rate for its service area keeps the charge it was booked at and nothing above
        -- the included weight, so a reweigh cannot invent a price the merchant never agreed to
        UPDATE
            Parcels.Parcel
        SET
            IncludedWeightGrams = WeightGrams,
            BaseCharge = DeliveryCharge,
            ExtraKgCharge = 0
        WHERE
            IncludedWeightGrams IS NULL;

        ALTER TABLE Parcels.Parcel ALTER COLUMN [IncludedWeightGrams] INT NOT NULL;
        ALTER TABLE Parcels.Parcel ALTER COLUMN [BaseCharge] DECIMAL (10, 2) NOT NULL;
        ALTER TABLE Parcels.Parcel ALTER COLUMN [ExtraKgCharge] DECIMAL (10, 2) NOT NULL;');
END;
