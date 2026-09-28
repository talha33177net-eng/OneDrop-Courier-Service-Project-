-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: Pre/001_GroupExistingOrders.sql
-- Purpose: Orders.Order.DeliveryGroupId becomes NOT NULL. SqlPackage refuses NULL -> NOT NULL on a table with rows
--          whatever they hold, so this script does it deliberately in the pre-schema phase ("dbup.exe pre"), on the
--          schema of task 2.1 (column present and nullable): it gives every order saved before grouping existed a
--          group, then drops the column's index and foreign key (both block ALTER COLUMN) and makes it NOT NULL.
--          The publish that follows recreates the index and the foreign key from the table file.
--          Orders that wait for a group get one group per customer and
--          address, opened at the first order and locked at the start of delivery day in the tenant's time zone
--          after the tenant's GroupJoinDays; it is Open only while that moment is still ahead. Deliver fast and
--          Don't hold orders get a group each, locked at once and delivered the next day, as the code does.
--          Guarded and in dynamic SQL, so it compiles and does nothing on a database without the nullable column
--          (a new database, or one already done).
--          SQL Server's AT TIME ZONE takes Windows zone names; the tenants store IANA ids, translated below. An
--          id with no translation stops the script instead of guessing.
-- Author: Courier team
-- Date: 2026-09-28
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

SET NOCOUNT ON;

IF EXISTS (
    SELECT
        1
    FROM
        sys.columns C
    WHERE
        C.[object_id] = OBJECT_ID(N'Orders.Order') AND
        C.[name] = N'DeliveryGroupId' AND
        C.is_nullable = 1)
BEGIN
    EXEC sp_executesql N'
    DECLARE @Now DATETIME2 (7) = SYSUTCDATETIME();
    DECLARE @Map TABLE (TenantId BIGINT, CustomerId BIGINT, AddressId BIGINT, GroupKey BIGINT, DeliveryGroupId BIGINT);

    SELECT
        T.Id AS TenantId,
        CASE T.TimeZone
            WHEN N''Asia/Dhaka'' THEN N''Bangladesh Standard Time''
            ELSE T.TimeZone
        END AS WindowsZone,
        T.GroupJoinDays
    INTO
        #TenantZone
    FROM
        Platform.Tenant T;

    IF EXISTS (
        SELECT
            1
        FROM
            #TenantZone Z
        WHERE
            NOT EXISTS (SELECT 1 FROM sys.time_zone_info I WHERE I.[name] = Z.WindowsZone) AND
            EXISTS (SELECT 1 FROM Orders.[Order] O WHERE O.TenantId = Z.TenantId AND O.DeliveryGroupId IS NULL))
    BEGIN
        THROW 50001, N''A tenant with ungrouped orders has a time zone this script cannot translate.'', 1;
    END;

    -- One row per group to create: waiting orders share a group per customer and address (GroupKey 0),
    -- every order that does not wait is its own group (GroupKey = its id)
    WITH Source AS (
        SELECT
            O.TenantId,
            O.CustomerId,
            O.AddressId,
            CASE WHEN O.Speed = 1 AND O.DoNotHold = 0 THEN 0 ELSE O.Id END AS GroupKey,
            O.Created
        FROM
            Orders.[Order] O
        WHERE
            O.DeliveryGroupId IS NULL
    ),
    Groups AS (
        SELECT
            S.TenantId,
            S.CustomerId,
            S.AddressId,
            S.GroupKey,
            MIN(S.Created) AS OpenedOn
        FROM
            Source S
        GROUP BY
            S.TenantId,
            S.CustomerId,
            S.AddressId,
            S.GroupKey
    ),
    Timed AS (
        SELECT
            G.*,
            H.Id AS HubId,
            CAST(
                CAST(
                    DATEADD(
                        DAY,
                        CASE WHEN G.GroupKey = 0 THEN Z.GroupJoinDays ELSE 1 END,
                        CAST(CAST(G.OpenedOn AS DATETIME2 (7)) AT TIME ZONE ''UTC'' AT TIME ZONE Z.WindowsZone AS DATE))
                    AS DATETIME2 (7))
                AT TIME ZONE Z.WindowsZone AT TIME ZONE ''UTC'' AS DATETIME2 (7)) AS LocksAt
        FROM
            Groups G
            JOIN
                #TenantZone Z
                ON Z.TenantId = G.TenantId
            JOIN
                Customers.CustomerAddress A
                ON A.Id = G.AddressId AND A.TenantId = G.TenantId
            JOIN
                Network.Area R
                ON R.Id = A.AreaId AND R.TenantId = G.TenantId
            JOIN
                Network.Zone N
                ON N.Id = R.ZoneId AND N.TenantId = G.TenantId
            JOIN
                Network.Hub H
                ON H.Id = N.HubId AND H.TenantId = G.TenantId
    )
    MERGE INTO Grouping.DeliveryGroup AS Target
    USING Timed AS S
    ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT
            (TenantId, CustomerId, AddressId, HubId, [Status], OpenedOn, LocksAt, LockedOn)
        VALUES
            (
                S.TenantId,
                S.CustomerId,
                S.AddressId,
                S.HubId,
                CASE WHEN S.GroupKey = 0 AND S.LocksAt > @Now THEN 1 ELSE 2 END,
                S.OpenedOn,
                S.LocksAt,
                CASE
                    WHEN S.GroupKey <> 0 THEN S.OpenedOn
                    WHEN S.LocksAt > @Now THEN NULL
                    ELSE S.LocksAt
                END
            )
    OUTPUT
        S.TenantId,
        S.CustomerId,
        S.AddressId,
        S.GroupKey,
        inserted.Id
    INTO
        @Map;

    UPDATE
        O
    SET
        DeliveryGroupId = M.DeliveryGroupId
    FROM
        Orders.[Order] O
        JOIN
            @Map M
            ON M.TenantId = O.TenantId AND
                M.CustomerId = O.CustomerId AND
                M.AddressId = O.AddressId AND
                M.GroupKey = CASE WHEN O.Speed = 1 AND O.DoNotHold = 0 THEN 0 ELSE O.Id END
    WHERE
        O.DeliveryGroupId IS NULL;

    IF EXISTS (SELECT 1 FROM Orders.[Order] WHERE DeliveryGroupId IS NULL)
    BEGIN
        THROW 50002, N''Some orders could not be given a delivery group.'', 1;
    END;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N''Orders.Order'') AND [name] = N''IX_Order_DeliveryGroupId'')
    BEGIN
        DROP INDEX IX_Order_DeliveryGroupId ON Orders.[Order];
    END;

    IF OBJECT_ID(N''Orders.FK_Order_DeliveryGroup'', N''F'') IS NOT NULL
    BEGIN
        ALTER TABLE Orders.[Order] DROP CONSTRAINT FK_Order_DeliveryGroup;
    END;

    ALTER TABLE Orders.[Order] ALTER COLUMN DeliveryGroupId BIGINT NOT NULL;';
END;
