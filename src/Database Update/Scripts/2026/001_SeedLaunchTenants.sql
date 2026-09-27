-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 001_SeedLaunchTenants.sql
-- Purpose: The two launch tenants with their hubs, zones and area lists.
--          OneDrop Dhaka: 7 zones on 5 hubs (Mohammadpur is served by the Dhanmondi hub, Banani by Gulshan),
--          60 + 25 per extra shop. OneDrop Chattogram: 5 zones on 2 hubs, with its own prices (70 + 30) so the
--          demo can show that tenants really are separate.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
DECLARE @Dhaka BIGINT;
DECLARE @Chattogram BIGINT;

INSERT INTO Platform.Tenant
    ([Name], Slug, TimeZone, CurrencyCode, SmsSenderName, BaseDeliveryFee, ExtraShopFee, FastDeliveryFee, GroupJoinDays)
VALUES
    (N'OneDrop Dhaka', N'dhaka', N'Asia/Dhaka', N'BDT', N'OneDrop', 60, 25, 60, 2);

SET @Dhaka = SCOPE_IDENTITY();

INSERT INTO Platform.Tenant
    ([Name], Slug, TimeZone, CurrencyCode, SmsSenderName, BaseDeliveryFee, ExtraShopFee, FastDeliveryFee, GroupJoinDays)
VALUES
    (N'OneDrop Chattogram', N'chattogram', N'Asia/Dhaka', N'BDT', N'OneDropCTG', 70, 30, 80, 2);

SET @Chattogram = SCOPE_IDENTITY();

-- Hubs
INSERT INTO Network.Hub
    (TenantId, Code, [Name], [Address])
VALUES
    (@Dhaka,      N'MIR', N'Mirpur hub',     N'Road 3, Section 10, Mirpur, Dhaka 1216'),
    (@Dhaka,      N'UTT', N'Uttara hub',     N'Sector 7, Uttara, Dhaka 1230'),
    (@Dhaka,      N'DHN', N'Dhanmondi hub',  N'Road 27, Dhanmondi, Dhaka 1209'),
    (@Dhaka,      N'GUL', N'Gulshan hub',    N'Gulshan Avenue, Gulshan 1, Dhaka 1212'),
    (@Dhaka,      N'MOT', N'Motijheel hub',  N'Dilkusha C/A, Motijheel, Dhaka 1000'),
    (@Chattogram, N'AGR', N'Agrabad hub',    N'Agrabad C/A, Chattogram 4100'),
    (@Chattogram, N'PAN', N'Panchlaish hub', N'O.R. Nizam Road, Panchlaish, Chattogram 4203');

-- Zones, each served by one hub
INSERT INTO Network.Zone
    (TenantId, Code, [Name], HubId)
SELECT
    Z.TenantId,
    Z.Code,
    Z.[Name],
    H.Id
FROM
    (VALUES
        (@Dhaka,      N'MIR', N'Mirpur',      N'MIR'),
        (@Dhaka,      N'UTT', N'Uttara',      N'UTT'),
        (@Dhaka,      N'MOH', N'Mohammadpur', N'DHN'),
        (@Dhaka,      N'DHN', N'Dhanmondi',   N'DHN'),
        (@Dhaka,      N'GUL', N'Gulshan',     N'GUL'),
        (@Dhaka,      N'BAN', N'Banani',      N'GUL'),
        (@Dhaka,      N'MOT', N'Motijheel',   N'MOT'),
        (@Chattogram, N'AGR', N'Agrabad',     N'AGR'),
        (@Chattogram, N'HAL', N'Halishahar',  N'AGR'),
        (@Chattogram, N'PAN', N'Panchlaish',  N'PAN'),
        (@Chattogram, N'CHK', N'Chawkbazar',  N'PAN'),
        (@Chattogram, N'NAS', N'Nasirabad',   N'PAN')
    ) Z (TenantId, Code, [Name], HubCode)
    INNER JOIN
        Network.Hub H
    ON H.TenantId = Z.TenantId AND
       H.Code = Z.HubCode;

-- The area list addresses pick from
INSERT INTO Network.Area
    (TenantId, ZoneId, [Name])
SELECT
    A.TenantId,
    Z.Id,
    A.[Name]
FROM
    (VALUES
        (@Dhaka,      N'MIR', N'Mirpur 1'),
        (@Dhaka,      N'MIR', N'Mirpur 2'),
        (@Dhaka,      N'MIR', N'Mirpur 10'),
        (@Dhaka,      N'MIR', N'Mirpur 11'),
        (@Dhaka,      N'MIR', N'Mirpur 12'),
        (@Dhaka,      N'MIR', N'Pallabi'),
        (@Dhaka,      N'MIR', N'Kazipara'),
        (@Dhaka,      N'MIR', N'Shewrapara'),
        (@Dhaka,      N'UTT', N'Uttara Sector 4'),
        (@Dhaka,      N'UTT', N'Uttara Sector 7'),
        (@Dhaka,      N'UTT', N'Uttara Sector 10'),
        (@Dhaka,      N'UTT', N'Uttara Sector 13'),
        (@Dhaka,      N'UTT', N'Diabari'),
        (@Dhaka,      N'MOH', N'Mohammadpur'),
        (@Dhaka,      N'MOH', N'Shyamoli'),
        (@Dhaka,      N'MOH', N'Adabor'),
        (@Dhaka,      N'MOH', N'Lalmatia'),
        (@Dhaka,      N'DHN', N'Dhanmondi'),
        (@Dhaka,      N'DHN', N'Kalabagan'),
        (@Dhaka,      N'DHN', N'Jigatola'),
        (@Dhaka,      N'DHN', N'Science Lab'),
        (@Dhaka,      N'GUL', N'Gulshan 1'),
        (@Dhaka,      N'GUL', N'Gulshan 2'),
        (@Dhaka,      N'GUL', N'Niketan'),
        (@Dhaka,      N'GUL', N'Baridhara'),
        (@Dhaka,      N'BAN', N'Banani'),
        (@Dhaka,      N'BAN', N'Banani DOHS'),
        (@Dhaka,      N'BAN', N'Mohakhali'),
        (@Dhaka,      N'MOT', N'Motijheel'),
        (@Dhaka,      N'MOT', N'Paltan'),
        (@Dhaka,      N'MOT', N'Arambagh'),
        (@Dhaka,      N'MOT', N'Kamalapur'),
        (@Chattogram, N'AGR', N'Agrabad'),
        (@Chattogram, N'AGR', N'Double Mooring'),
        (@Chattogram, N'AGR', N'Chowmuhani'),
        (@Chattogram, N'HAL', N'Halishahar'),
        (@Chattogram, N'HAL', N'Patenga'),
        (@Chattogram, N'PAN', N'Panchlaish'),
        (@Chattogram, N'PAN', N'Probartak'),
        (@Chattogram, N'PAN', N'GEC Circle'),
        (@Chattogram, N'CHK', N'Chawkbazar'),
        (@Chattogram, N'CHK', N'Jamalkhan'),
        (@Chattogram, N'CHK', N'Anderkilla'),
        (@Chattogram, N'NAS', N'Nasirabad'),
        (@Chattogram, N'NAS', N'Khulshi'),
        (@Chattogram, N'NAS', N'Bayezid')
    ) A (TenantId, ZoneCode, [Name])
    INNER JOIN
        Network.Zone Z
    ON Z.TenantId = A.TenantId AND
       Z.Code = A.ZoneCode;
