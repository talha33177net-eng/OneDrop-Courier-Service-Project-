-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- SEQUENCE: Grouping.DeliveryGroupNumber
-- Purpose: Public delivery group numbers (DG-100001), drawn by the default on Grouping.DeliveryGroup.Number.
--          Shared by all tenants so a number never repeats; gaps after a rolled-back insert are expected.
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE SEQUENCE [Grouping].[DeliveryGroupNumber]
    AS BIGINT
    START WITH 100001
    INCREMENT BY 1;
