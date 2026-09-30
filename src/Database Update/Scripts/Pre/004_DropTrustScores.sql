-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Script: Pre/004_DropTrustScores.sql
-- Purpose: Customers.Customer.TrustScore and Merchants.Merchant.ReliabilityScore were never used (every row holds
--          100): task 3.8 works out a customer's advance payment and a shop's drop-off from their history instead.
--          Dropping a column is data loss, which the publish refuses, so it is done here: each column's unnamed
--          default constraint first, then the column.
--          Guarded and in dynamic SQL, so it does nothing on a new database or one where the columns are gone.
-- Author: Courier team
-- Date: 2026-09-30
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

SET NOCOUNT ON;

DECLARE @Drops TABLE (TableName SYSNAME, ColumnName SYSNAME);
INSERT INTO @Drops (TableName, ColumnName)
VALUES
    (N'Customers.Customer', N'TrustScore'),
    (N'Merchants.Merchant', N'ReliabilityScore');

DECLARE @Sql NVARCHAR (MAX) = N'';

SELECT
    @Sql = @Sql + N'ALTER TABLE ' + D.TableName + N' DROP CONSTRAINT ' + QUOTENAME(DC.name) + N'; '
FROM
    @Drops D
    INNER JOIN
        sys.default_constraints DC
    ON DC.parent_object_id = OBJECT_ID(D.TableName) AND
        DC.parent_column_id = COLUMNPROPERTY(OBJECT_ID(D.TableName), D.ColumnName, 'ColumnId');

SELECT
    @Sql = @Sql + N'ALTER TABLE ' + D.TableName + N' DROP COLUMN ' + QUOTENAME(D.ColumnName) + N'; '
FROM
    @Drops D
WHERE
    COL_LENGTH(D.TableName, D.ColumnName) IS NOT NULL;

IF @Sql <> N''
BEGIN
    EXEC sp_executesql @Sql;
END;
