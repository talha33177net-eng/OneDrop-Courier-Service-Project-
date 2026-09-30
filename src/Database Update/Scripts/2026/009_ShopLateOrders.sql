-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: 009_ShopLateOrders.sql
-- Purpose: Orders.Order.ShopLateOn for the orders left behind before task 3.8: an order its shop was charged the
--          late-handover fee for (Payments.LedgerEntry Kind 3) was left behind through the shop, when it was left
--          behind (LeftBehindOn)
-- Author: Courier team
-- Date: 2026-09-30
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
UPDATE
    O
SET
    ShopLateOn = O.LeftBehindOn,
    UpdatedOn = GETUTCDATE()
FROM
    Orders.[Order] O
    INNER JOIN
        Payments.LedgerEntry L
    ON L.OrderId = O.Id AND
        L.TenantId = O.TenantId AND
        L.Kind = 3
WHERE
    O.ShopLateOn IS NULL AND
    O.LeftBehindOn IS NOT NULL;
