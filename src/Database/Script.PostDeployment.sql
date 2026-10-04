/*
Post-Deployment Script
--------------------------------------------------------------------------------------
 Appended to every publish. Use SQLCMD syntax to include a file: :r .\myfile.sql
--------------------------------------------------------------------------------------
*/

-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- Baseline data
--
-- The rows the app cannot run without, so that publishing the project produces a usable database. Today that
-- is the role list: every login needs a role, and the web app authorises by these exact names
-- (Application.Common.Roles).
--
-- Insert-if-missing rather than MERGE: this runs against every environment on every publish and must never
-- overwrite a value an environment has changed. Existing rows are left alone.
--
-- Tenant data (tenants, hubs, zones, areas) is deliberately NOT here: it is launch data, added once per
-- environment by the DbUp script 2026/001_SeedCourier.sql. Demo logins and merchants are created by the
-- web app's DemoDataSeeder in Development only, because passwords need Identity's hasher.
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
PRINT 'Seeding baseline data...';

WITH Baseline([Name]) AS
(
    SELECT N'PlatformAdmin' UNION ALL
    SELECT N'TenantAdmin'   UNION ALL
    SELECT N'Merchant'      UNION ALL
    SELECT N'HubStaff'      UNION ALL
    SELECT N'Rider'
)
INSERT INTO [Identity].[Role]
    ([Name], [NormalizedName], [ConcurrencyStamp])
SELECT
    B.[Name],
    UPPER(B.[Name]),
    CONVERT(NVARCHAR (36), NEWID())
FROM
    Baseline B
WHERE
    NOT EXISTS (SELECT 1 FROM [Identity].[Role] R WHERE R.NormalizedName = UPPER(B.[Name]));
