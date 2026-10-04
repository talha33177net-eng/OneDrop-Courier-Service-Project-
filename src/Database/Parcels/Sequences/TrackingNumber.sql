-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- SEQUENCE: Parcels.TrackingNumber
-- Purpose: Public tracking codes (OD10000001), drawn by the default on Parcels.Parcel.TrackingCode. Shared by all
--          tenants so a code never repeats; gaps after a rolled-back insert are expected.
-- Author: Courier team
-- Date: 2026-10-04
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE SEQUENCE [Parcels].[TrackingNumber]
    AS BIGINT
    START WITH 10000001
    INCREMENT BY 1;
