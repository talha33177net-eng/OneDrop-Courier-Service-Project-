-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: Pre/002_PriceExistingOrders.sql
-- Purpose: Orders.Order.AddedFee is NOT NULL with no default (a fee is always the tenant's, never a fallback), and
--          SqlPackage cannot add such a column to a table with rows. This script adds it nullable, gives every
--          order saved before pricing the fee it would have been given on arrival, and makes it NOT NULL; the
--          publish that follows then shapes the column and its check as the table file says.
--          The fee an order added, taking its group's orders in arrival (Id) order, at the tenant's prices:
--          Deliver fast = FastDeliveryFee; a shop's first order in the group = BaseDeliveryFee when it is the
--          group's first shop, ExtraShopFee otherwise; any later order from the same shop = 0. The same rule as
--          Domain.Pricing.DeliveryFeeCalculator.AddedFee.
--          Guarded and in dynamic SQL, so it does nothing on a new database or one that already has the column.
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

SET NOCOUNT ON;

IF OBJECT_ID(N'Orders.Order', N'U') IS NOT NULL AND
    COL_LENGTH(N'Orders.Order', N'AddedFee') IS NULL
BEGIN
    EXEC sp_executesql N'ALTER TABLE Orders.[Order] ADD AddedFee DECIMAL (10, 2) NULL;';

    EXEC sp_executesql N'
    WITH Numbered AS (
        SELECT
            O.Id,
            O.DeliveryGroupId,
            O.Speed,
            ROW_NUMBER() OVER (PARTITION BY O.DeliveryGroupId, O.MerchantId ORDER BY O.Id) AS ShopOrder
        FROM
            Orders.[Order] O
    ),
    Ranked AS (
        SELECT
            N.Id,
            N.Speed,
            N.ShopOrder,
            SUM(CASE WHEN N.ShopOrder = 1 THEN 1 ELSE 0 END)
                OVER (PARTITION BY N.DeliveryGroupId ORDER BY N.Id ROWS UNBOUNDED PRECEDING) AS ShopNumber
        FROM
            Numbered N
    )
    UPDATE
        O
    SET
        AddedFee =
            CASE
                WHEN R.Speed = 2 THEN T.FastDeliveryFee
                WHEN R.ShopOrder > 1 THEN 0
                WHEN R.ShopNumber = 1 THEN T.BaseDeliveryFee
                ELSE T.ExtraShopFee
            END
    FROM
        Orders.[Order] O
        JOIN
            Ranked R
            ON R.Id = O.Id
        JOIN
            Platform.Tenant T
            ON T.Id = O.TenantId;

    ALTER TABLE Orders.[Order] ALTER COLUMN AddedFee DECIMAL (10, 2) NOT NULL;';
END;
