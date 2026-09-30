-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- FUNCTION: Platform.TenantAccess
-- Purpose: Row-Level Security predicate (isolation layer 3): may this connection see or write a row of @TenantId?
--          The application marks every connection it opens (SESSION_CONTEXT TenantScoped = 1, read only) and names
--          its tenant (TenantId, read only), so its rows are the tenant's only, and none when it has no tenant.
--          AllTenants = 1 is set, for the duration of one read, by code that crosses tenants on purpose (the API
--          key lookup, the platform page). A NULL TenantId is a platform login (Identity.User), seen only by a
--          connection without a tenant. A connection that is not marked (DbUp, SqlPackage, an operator's query) is
--          not restricted.
-- Author: Courier team
-- Date: 2026-09-30
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE FUNCTION [Platform].[TenantAccess] (@TenantId BIGINT)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT
        1 AS Allowed
    WHERE
        SESSION_CONTEXT(N'TenantScoped') IS NULL OR
        CAST(SESSION_CONTEXT(N'AllTenants') AS BIT) = 1 OR
        @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS BIGINT) OR
        (@TenantId IS NULL AND SESSION_CONTEXT(N'TenantId') IS NULL);
