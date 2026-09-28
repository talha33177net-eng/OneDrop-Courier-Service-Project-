# Data migration scripts (DbUp)

`dbup.exe` runs every script here **once per database** and records it in `dbo.SchemaVersions`.

## Where does my change go?

| Change | Where |
|---|---|
| New table, column, index, view, procedure, sequence | `src/Database` — edit the object's own file |
| Seed or reference data, moving or fixing data | Here: `Scripts/[Year]/NNN_PascalCaseName.sql` |
| Intentional data loss (drop or rename a column), or a change SqlPackage would block on a table with rows: NULL → NOT NULL, or a new NOT NULL column with no default | Here: `Scripts/Pre/NNN_Name.sql` — runs **before** the publish, against the old schema; guard every step so it also runs on a new database. A new column is added nullable, filled, then made NOT NULL; its table file still defines it |
| Ad-hoc analysis or support queries | `sql/` at the repository root (never deployed) |

Never write `ALTER TABLE ... ADD` in a Year script: add the column to the table file and let the publish diff it.

## Deploy order

```
dbup pre       Scripts/Pre only, against the old schema (creates an empty database the first time)
publish        the dacpac from src/Database
dbup           everything else
```

`tools/db/publish.ps1` runs all three.

## Rules

- Name scripts `NNN_DescriptiveName.sql` (3 digits). They are embedded resources and run in name order.
- Each script runs in its own transaction. **No `GO`.**
- Schema-qualify every object: `Network.Zone`, never `Zone`.
- No idempotency checks needed — DbUp never runs a script twice.
- Never edit a script after it has run on a shared database. Write a new one.
- Start the file with the standard header block (`-- Script: 002_Name.sql`).
