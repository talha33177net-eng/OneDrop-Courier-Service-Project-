-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- SEQUENCE: Payments.OnlinePaymentNumber
-- Purpose: Online payment numbers (PAY-100001), drawn by the default on Payments.OnlinePayment.Number. Shared by all
--          tenants.
-- Author: Courier team
-- Date: 2026-10-08
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE SEQUENCE [Payments].[OnlinePaymentNumber]
    AS BIGINT
    START WITH 100001
    INCREMENT BY 1;
