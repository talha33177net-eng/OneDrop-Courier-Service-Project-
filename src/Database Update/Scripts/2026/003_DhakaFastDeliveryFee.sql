-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 003_DhakaFastDeliveryFee.sql
-- Purpose: OneDrop Dhaka's Deliver fast fee from Tk 60 to Tk 70 (market review, 2026-09-28). At Tk 60 next-day
--          delivery cost the same as waiting for the Day 3 delivery, so nobody had a reason to wait for other shops
--          to join. Fast is at the door the day after the order (same day from pickup), which Dhaka couriers sell
--          for about Tk 105; Chattogram already charges its base fee + Tk 10. Orders already placed keep their fee.
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE
    Platform.Tenant
SET
    FastDeliveryFee = 70,
    UpdatedOn = GETUTCDATE()
WHERE
    Slug = N'dhaka';
