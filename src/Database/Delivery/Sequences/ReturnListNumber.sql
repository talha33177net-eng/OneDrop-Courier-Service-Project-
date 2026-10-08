-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- SEQUENCE: Delivery.ReturnListNumber
-- Purpose: Return list numbers (RL-100001), drawn by the default on Delivery.ReturnList.Number. Shared by all tenants.
-- Author: Courier team
-- Date: 2026-10-07
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE SEQUENCE [Delivery].[ReturnListNumber]
    AS BIGINT
    START WITH 100001
    INCREMENT BY 1;
