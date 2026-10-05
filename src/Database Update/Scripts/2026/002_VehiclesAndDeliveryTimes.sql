-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 002_VehiclesAndDeliveryTimes.sql
-- Purpose: OneDrop Courier's vehicle loads and delivery times. At once a bicycle carries 25 parcels, 15 kg in all and
--          5 kg a parcel; a motorbike (two carrier bags) 40 parcels, 30 kg and 10 kg; a pickup van 300 parcels, a
--          tonne and the heaviest parcel booked (30 kg). Parcels are delivered the day after pickup inside the city,
--          in two days to a suburb and in three outside the city (the Dhaka market promises 24 hours inside Dhaka and
--          72 hours outside, 2026). Riders are due back at their hub at 8 pm with the day's cash. The courier's admin
--          changes the loads on the Riders page and the delivery times on the Rates page.
-- Author: Courier team
-- Date: 2026-10-05
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
DECLARE @Courier BIGINT = (SELECT Id FROM Platform.Tenant WHERE Slug = N'onedrop');

INSERT INTO Delivery.VehicleCapacity
    (TenantId, Vehicle, MaxParcels, MaxLoadGrams, MaxParcelGrams)
VALUES
    (@Courier, 1, 25,  15000,   5000),
    (@Courier, 2, 40,  30000,   10000),
    (@Courier, 3, 300, 1000000, 30000);

UPDATE Pricing.DeliveryRate
SET
    DeliveryDays = CASE ServiceArea WHEN 1 THEN 1 WHEN 2 THEN 2 ELSE 3 END
WHERE
    TenantId = @Courier;

UPDATE Platform.Tenant
SET
    RiderReturnTime = '20:00'
WHERE
    Id = @Courier;
