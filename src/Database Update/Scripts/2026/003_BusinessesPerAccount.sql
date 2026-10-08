-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 003_BusinessesPerAccount.sql
-- Purpose: OneDrop Courier lets a merchant account add up to 10 businesses besides its main profile, as Steadfast
--          does (2026). Each business has its own parcels, pickups, payments and balance under the one login.
-- Author: Courier team
-- Date: 2026-10-06
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE Platform.Tenant
SET
    MaxBusinessesPerAccount = 10
WHERE
    Slug = N'onedrop';
