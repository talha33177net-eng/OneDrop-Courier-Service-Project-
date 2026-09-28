# Database conventions

Adapted from DCN's database rules. The schema is owned by the SQL project; EF Core maps onto it and never
migrates. Follow the existing files as the reference.

## Where things live

| What | Where |
|---|---|
| Tables, sequences, views, procedures | `src/Database/<Schema>/Tables`, `Sequences`, `Views`, `Stored Procedures` |
| Schema DDL | `src/Database/Security/<Schema>.sql` |
| Baseline rows every database needs (roles) | `src/Database/Script.PostDeployment.sql` (insert-if-missing, never overwrite) |
| Seed and data migrations (DbUp) | `src/Database Update/Scripts/<Year>/NNN_PascalCaseName.sql` |
| Intentional data loss (drop or rename a column), NULL → NOT NULL, or a new NOT NULL column with no default, on a table with rows (add or backfill + ALTER, guarded) | `src/Database Update/Scripts/Pre/NNN_Name.sql` |
| EF mapping | `src/Infrastructure/Persistence/Configurations/<Module>Configuration.cs` |
| Ad-hoc analysis SQL | `sql/` at the repository root (never deployed) |

One schema per module: `Platform`, `Identity`, `Network`, `Customers`, `Merchants`, `Orders`, and later
`Grouping`, `Pricing`, `Delivery`, `Payments`, `Notifications`. Schema names for C# are in `Schemas`
(`Persistence/Configurations/MappingExtensions.cs`).

## Adding a column or table

1. Edit or add the table file in the SQL project. Never write `ALTER TABLE` scripts for columns.
2. A new `.sql` file must be listed once as `<Build Include="Schema/Type/File.sql" />` in
   `Database.sqlproj`. `DatabaseProjectFileTests` fails on a missing, extra or duplicate entry.
3. Map it in the EF configuration (max lengths, precision, column types must match).
4. `./tools/db/publish.ps1` to deploy locally; `SchemaMatchesModelTests` confirms EF and the table agree.

## SQL project rules

- Target is Azure SQL (`SqlAzureV12DatabaseSchemaProvider`), collation `SQL_Latin1_General_CP1_CI_AS`. Nothing
  may depend on a feature Azure SQL lacks. `Microsoft.Build.Sql` stays pinned at 2.1.0 (see the sqlproj).
- Indexes are declared in the table file after a `GO`, never in a separate file. Deleting one from the file is
  how it is dropped.
- Production publishes block on possible data loss. Drops and renames go in `Scripts/Pre`.

## DbUp scripts

- Data only. No `GO`. One transaction per script. Schema-qualify every object.
- Name `NNN_DescriptiveName.sql`; they run once per database, in name order. No idempotency checks needed.
- Never edit a script after it has run on a shared database.

## File header

Every file in the SQL project and in DbUp starts with this block (label `TABLE`, `SEQUENCE`, `VIEW`, `PROCEDURE`,
`FUNCTION`, `SCHEMA`, or `Script` for migrations). Later changes are dated lines inside the header.

```sql
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Orders.Order
-- Purpose: What the object is for and any non-obvious behaviour
--          continuation lines aligned under the text
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
```

## Tables

- `CREATE TABLE [Schema].[Name] (` with every column and constraint bracketed; table name = C# class name.
- Column names padded so data types line up.
- `[Id] BIGINT IDENTITY (1, 1)`; every id and foreign key is `BIGINT` (Identity's claim ids are the INT exception).
- `NVARCHAR (300)` with a space before the parenthesis; no `VARCHAR`. Money `DECIMAL (12, 2)`.
- Defaults use double parentheses: `DEFAULT ((0))`. Enums are `TINYINT`, never renumbered.
- Primary key is the unnamed `PRIMARY KEY CLUSTERED ([Id] ASC)`.
- Foreign keys are always named `FK_Table_ReferencedTable` (`FK_Table_ReferencedTable_Column` when the same
  target is referenced twice). Unique constraints/indexes `UX_Table_Purpose`, indexes `IX_Table_Purpose`,
  checks `chk_Table_Rule`, named defaults `DF_Table_Column`.

### Tenancy and housekeeping columns

Every table except `Platform.*` and the Identity join tables carries `[TenantId] BIGINT NOT NULL` with
`FK_Table_Tenant` to `Platform.Tenant`, and an index that leads with `TenantId` (a unique index starting with it
counts). Merchant-owned tables also carry `[MerchantId]` - copy it onto child rows so the merchant filter needs
no join.

Column order: `Id`, `TenantId`, business columns, `Archived BIT DEFAULT ((0)) NOT NULL` (when rows are hidden
rather than deleted), `UpdatedId BIGINT NULL` (`FK_Table_User` to `Identity.User`),
`UpdatedOn DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL`, `Created DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL`.
The save interceptor fills them from C#; the defaults are for rows inserted by SQL scripts.

## Query bodies (views, procedures, migrations)

- Every keyword clause on its own line, operands indented one level; joins are three lines (join, table, `ON`).
- `AND`/`OR` trail the line. Reference objects as `Schema.Table Alias`; bracket only reserved words.
- Tenant scoping is applied explicitly in `WHERE` (`@TenantId`); never trust an id alone.
