-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 004_WeightAllowance.sql
-- Purpose: The weight each shop's parcels may have in one delivery before a fee per started kilogram is charged
--          (market review, 2026-09-28; values confirmed by the owner 2026-09-29). Every Dhaka courier prices by
--          weight (Pathao Tk 60 up to 500 g, Tk 90 at 2 kg, +Tk 15 per kg). OneDrop Dhaka: 2 kg, then Tk 15 per
--          started kg. OneDrop Chattogram: 2 kg, then Tk 20, in line with its higher prices. Orders already placed
--          keep the fee they were given.
-- Author: Courier team
-- Date: 2026-09-29
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE
    T
SET
    WeightAllowanceGrams = S.WeightAllowanceGrams,
    ExtraKgFee = S.ExtraKgFee,
    UpdatedOn = GETUTCDATE()
FROM
    Platform.Tenant T
    INNER JOIN
        (VALUES
            (N'dhaka',      2000, 15),
            (N'chattogram', 2000, 20)
        ) S (Slug, WeightAllowanceGrams, ExtraKgFee)
    ON S.Slug = T.Slug;
