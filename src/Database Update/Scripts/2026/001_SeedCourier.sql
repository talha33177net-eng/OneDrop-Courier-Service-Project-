-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 001_SeedCourier.sql
-- Purpose: The launch courier, OneDrop Courier, with its coverage map and rate card. Dhaka city has 9 zones on 5 hubs,
--          its suburbs (Savar, Gazipur, Narayanganj, Keraniganj) 4 zones on 3 hubs, and 8 divisional cities a hub and
--          zone each. Rates from the Dhaka market (couriers' own pages, 2026): inside the city ৳60 for the first kg and
--          ৳15 per kg after, suburbs ৳100 + ৳20, outside the city ৳120 + ৳20; a 1% COD charge everywhere; a return
--          costs the delivery charge plus ৳0 / ৳50 / ৳60. The courier's admin changes the rates on its Rates page.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
DECLARE @Courier BIGINT;

INSERT INTO Platform.Tenant
    ([Name], Slug, TimeZone, CurrencyCode, SmsSenderName, SupportPhone, MaxDeliveryAttempts)
VALUES
    (N'OneDrop Courier', N'onedrop', N'Asia/Dhaka', N'BDT', N'OneDrop', N'09610-001122', 3);

SET @Courier = SCOPE_IDENTITY();

-- Hubs
INSERT INTO Network.Hub
    (TenantId, Code, [Name], [Address], Phone)
VALUES
    (@Courier, N'MIR', N'Mirpur hub',      N'Road 3, Section 10, Mirpur, Dhaka 1216',   N'01700-100101'),
    (@Courier, N'UTT', N'Uttara hub',      N'Sector 7, Uttara, Dhaka 1230',             N'01700-100102'),
    (@Courier, N'DHN', N'Dhanmondi hub',   N'Road 27, Dhanmondi, Dhaka 1209',           N'01700-100103'),
    (@Courier, N'GUL', N'Gulshan hub',     N'Gulshan Avenue, Gulshan 1, Dhaka 1212',    N'01700-100104'),
    (@Courier, N'MOT', N'Motijheel hub',   N'Dilkusha C/A, Motijheel, Dhaka 1000',      N'01700-100105'),
    (@Courier, N'SAV', N'Savar hub',       N'Bazar Road, Savar, Dhaka 1340',            N'01700-100106'),
    (@Courier, N'GAZ', N'Gazipur hub',     N'Chowrasta, Gazipur 1700',                  N'01700-100107'),
    (@Courier, N'NAR', N'Narayanganj hub', N'Chashara, Narayanganj 1400',               N'01700-100108'),
    (@Courier, N'CTG', N'Chattogram hub',  N'Agrabad C/A, Chattogram 4100',             N'01700-100109'),
    (@Courier, N'SYL', N'Sylhet hub',      N'Zindabazar, Sylhet 3100',                  N'01700-100110'),
    (@Courier, N'RAJ', N'Rajshahi hub',    N'Shaheb Bazar, Rajshahi 6100',              N'01700-100111'),
    (@Courier, N'KHU', N'Khulna hub',      N'KDA Avenue, Khulna 9100',                  N'01700-100112'),
    (@Courier, N'BAR', N'Barishal hub',    N'Sadar Road, Barishal 8200',                N'01700-100113'),
    (@Courier, N'RNG', N'Rangpur hub',     N'Jahaj Company More, Rangpur 5400',         N'01700-100114'),
    (@Courier, N'MYM', N'Mymensingh hub',  N'Ganginar Par, Mymensingh 2200',            N'01700-100115'),
    (@Courier, N'CUM', N'Cumilla hub',     N'Kandirpar, Cumilla 3500',                  N'01700-100116');

-- Zones, each served by one hub; City and IsSuburb decide the service area a parcel is charged at
INSERT INTO Network.Zone
    (TenantId, Code, [Name], HubId, City, IsSuburb)
SELECT
    @Courier,
    Z.Code,
    Z.[Name],
    H.Id,
    Z.City,
    Z.IsSuburb
FROM
    (VALUES
        (N'MIR', N'Mirpur',      N'MIR', N'Dhaka',      0),
        (N'UTT', N'Uttara',      N'UTT', N'Dhaka',      0),
        (N'MOH', N'Mohammadpur', N'DHN', N'Dhaka',      0),
        (N'DHN', N'Dhanmondi',   N'DHN', N'Dhaka',      0),
        (N'GUL', N'Gulshan',     N'GUL', N'Dhaka',      0),
        (N'BAN', N'Banani',      N'GUL', N'Dhaka',      0),
        (N'BAD', N'Badda',       N'GUL', N'Dhaka',      0),
        (N'MOT', N'Motijheel',   N'MOT', N'Dhaka',      0),
        (N'OLD', N'Old Dhaka',   N'MOT', N'Dhaka',      0),
        (N'SAV', N'Savar',       N'SAV', N'Dhaka',      1),
        (N'GAZ', N'Gazipur',     N'GAZ', N'Dhaka',      1),
        (N'NAR', N'Narayanganj', N'NAR', N'Dhaka',      1),
        (N'KER', N'Keraniganj',  N'NAR', N'Dhaka',      1),
        (N'CTG', N'Chattogram',  N'CTG', N'Chattogram', 0),
        (N'SYL', N'Sylhet',      N'SYL', N'Sylhet',     0),
        (N'RAJ', N'Rajshahi',    N'RAJ', N'Rajshahi',   0),
        (N'KHU', N'Khulna',      N'KHU', N'Khulna',     0),
        (N'BAR', N'Barishal',    N'BAR', N'Barishal',   0),
        (N'RNG', N'Rangpur',     N'RNG', N'Rangpur',    0),
        (N'MYM', N'Mymensingh',  N'MYM', N'Mymensingh', 0),
        (N'CUM', N'Cumilla',     N'CUM', N'Cumilla',    0)
    ) Z (Code, [Name], HubCode, City, IsSuburb)
    INNER JOIN
        Network.Hub H
    ON H.TenantId = @Courier AND
       H.Code = Z.HubCode;

-- The area list recipient addresses and pickup points pick from
INSERT INTO Network.Area
    (TenantId, ZoneId, [Name])
SELECT
    @Courier,
    Z.Id,
    A.[Name]
FROM
    (VALUES
        (N'MIR', N'Mirpur 1'),
        (N'MIR', N'Mirpur 2'),
        (N'MIR', N'Mirpur 10'),
        (N'MIR', N'Mirpur 11'),
        (N'MIR', N'Mirpur 12'),
        (N'MIR', N'Pallabi'),
        (N'MIR', N'Kazipara'),
        (N'MIR', N'Shewrapara'),
        (N'UTT', N'Uttara Sector 4'),
        (N'UTT', N'Uttara Sector 7'),
        (N'UTT', N'Uttara Sector 10'),
        (N'UTT', N'Uttara Sector 13'),
        (N'UTT', N'Diabari'),
        (N'UTT', N'Airport'),
        (N'MOH', N'Mohammadpur'),
        (N'MOH', N'Shyamoli'),
        (N'MOH', N'Adabor'),
        (N'MOH', N'Lalmatia'),
        (N'DHN', N'Dhanmondi'),
        (N'DHN', N'Kalabagan'),
        (N'DHN', N'Jigatola'),
        (N'DHN', N'Science Lab'),
        (N'DHN', N'Hazaribagh'),
        (N'GUL', N'Gulshan 1'),
        (N'GUL', N'Gulshan 2'),
        (N'GUL', N'Niketan'),
        (N'GUL', N'Baridhara'),
        (N'BAN', N'Banani'),
        (N'BAN', N'Banani DOHS'),
        (N'BAN', N'Mohakhali'),
        (N'BAN', N'Tejgaon'),
        (N'BAD', N'Badda'),
        (N'BAD', N'Rampura'),
        (N'BAD', N'Banasree'),
        (N'BAD', N'Khilgaon'),
        (N'MOT', N'Motijheel'),
        (N'MOT', N'Paltan'),
        (N'MOT', N'Arambagh'),
        (N'MOT', N'Kamalapur'),
        (N'MOT', N'Malibagh'),
        (N'OLD', N'Lalbagh'),
        (N'OLD', N'Wari'),
        (N'OLD', N'Sadarghat'),
        (N'OLD', N'Jatrabari'),
        (N'SAV', N'Savar'),
        (N'SAV', N'Ashulia'),
        (N'SAV', N'Hemayetpur'),
        (N'GAZ', N'Tongi'),
        (N'GAZ', N'Gazipur Sadar'),
        (N'GAZ', N'Board Bazar'),
        (N'NAR', N'Narayanganj Sadar'),
        (N'NAR', N'Fatullah'),
        (N'NAR', N'Siddhirganj'),
        (N'KER', N'Keraniganj'),
        (N'KER', N'Jinjira'),
        (N'CTG', N'Agrabad'),
        (N'CTG', N'Panchlaish'),
        (N'CTG', N'GEC Circle'),
        (N'CTG', N'Halishahar'),
        (N'CTG', N'Chawkbazar'),
        (N'CTG', N'Nasirabad'),
        (N'CTG', N'Patenga'),
        (N'CTG', N'Bayezid'),
        (N'SYL', N'Zindabazar'),
        (N'SYL', N'Amberkhana'),
        (N'SYL', N'Shahjalal Upashahar'),
        (N'SYL', N'Sylhet Sadar'),
        (N'RAJ', N'Shaheb Bazar'),
        (N'RAJ', N'Boalia'),
        (N'RAJ', N'Motihar'),
        (N'RAJ', N'Rajshahi Sadar'),
        (N'KHU', N'Khulna Sadar'),
        (N'KHU', N'Sonadanga'),
        (N'KHU', N'Khalishpur'),
        (N'KHU', N'Daulatpur'),
        (N'BAR', N'Barishal Sadar'),
        (N'BAR', N'Nathullabad'),
        (N'BAR', N'Rupatoli'),
        (N'RNG', N'Rangpur Sadar'),
        (N'RNG', N'Modern More'),
        (N'RNG', N'Dhap'),
        (N'MYM', N'Mymensingh Sadar'),
        (N'MYM', N'Charpara'),
        (N'MYM', N'Shambhuganj'),
        (N'CUM', N'Cumilla Sadar'),
        (N'CUM', N'Kandirpar'),
        (N'CUM', N'Tomsom Bridge')
    ) A (ZoneCode, [Name])
    INNER JOIN
        Network.Zone Z
    ON Z.TenantId = @Courier AND
       Z.Code = A.ZoneCode;

-- The rate card: 1 inside city, 2 suburb, 3 outside city
INSERT INTO Pricing.DeliveryRate
    (TenantId, ServiceArea, IncludedWeightGrams, BaseCharge, ExtraKgCharge, CodChargePercent, ReturnCharge)
VALUES
    (@Courier, 1, 1000, 60,  15, 1, 0),
    (@Courier, 2, 1000, 100, 20, 1, 50),
    (@Courier, 3, 1000, 120, 20, 1, 60);
