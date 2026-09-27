# Development conventions

Delivery-grouping platform: orders from different shops to the same customer travel in one delivery. Multi-tenant
modular monolith on .NET 10, SQL Server. The product is described in `README.md`, the 4-week plan and progress in
`Plans/Implementation-Plan.md`.

[Database.md](Database.md) is the authority for SQL, the SQL project and DbUp. It is not repeated here.

## Working rhythm

- **Start of the day:** read `Plans/Implementation-Plan.md` ("Today" section) and continue with the first
  unchecked task of the current week.
- **After every task:** run the plan's test routine — build, new tests for the new work, republish both databases
  if the schema changed, all three test suites green (integration must report *succeeded*, not *skipped*), and a
  live check in the running app. A task is not done until this passes.
- **Then:** tick the task in the plan, update "Today", and add the day's entry to the daily log with what was
  tested.

## Build and test

```bash
dotnet build Courier.sln
dotnet test --project tests/Domain.Tests
dotnet test --project tests/Architecture.Tests
dotnet test --project tests/Integration.Tests
```

- Tests run on Microsoft.Testing.Platform (`global.json`), so use `dotnet test --project <path>`.
- The integration suite runs against `OneDrop-Test` on ras-x2, named in the git-ignored
  `tests/Integration.Tests/testsettings.Local.json` (`INTEGRATION_TEST_DB` overrides it). With no database
  configured it skips itself, and a green run with everything skipped means nothing ran. After a schema change,
  update it with `./tools/db/publish.ps1 -Database OneDrop-Test`. Ask before creating any other database on ras-x2.
- Never point integration tests at the dev database `OneDrop`.
- Connection strings (they contain the password) live only in git-ignored `*.Local.json` files. Never commit one.
- No product-name prefix in code: projects, folders and namespaces are `Domain`, `Application`, `Infrastructure`,
  `Web`, `Database`, `Database Update` (like DCN). The OneDrop brand appears only in user-facing text and data.

## Architecture

- Domain → nothing. Application → Domain (+ EF Core for DbSet/LINQ only). Infrastructure → Application.
  Web → Infrastructure. Business rules never reference Infrastructure or Web.
- Use cases are vertical slices in `src/Application/<Module>/<UseCase>/`: command, validator, handler.
  Handlers are plain scoped classes registered in `Application/DependencyInjection.cs`; there is no mediator.
- A business "no" is a `Result` with an `Error` (code, message, type); exceptions are for bugs. The API maps
  errors to problem details in `Web/Api/ResultMapping.cs`.
- Aggregates guard their own rules: `Order.Create` validates, `Order.MoveTo` enforces the status state machine.
  Methods with five or more parameters take a record (`NewOrder`).

## Tenancy (read before touching data access)

- Every entity outside `Domain/Platform` derives from `TenantEntity`. `Architecture.Tests` fails otherwise.
- `TenantId`, `Created`, `UpdatedOn`, `UpdatedId` are stamped by `TenantSaveInterceptor`; never set them in code.
- Queries are filtered automatically (`AppDbContext` named filters `Tenant` and `Merchant`). Do not add
  `IgnoreQueryFilters()` without a filter key, and only where crossing tenants is the point (API key lookup,
  platform pages). Say why in a comment.
- A request's tenant comes from the subdomain (`TenantResolutionMiddleware`) or the API key
  (`ApiKeyAuthenticationHandler`). Background work sets `TenantContext` from a job parameter.
- A merchant must only ever see its own orders; another tenant's or merchant's row is a 404, never a 403.

## C# conventions

`.editorconfig` covers formatting (it is DCN's). House rules it cannot express:

- Latest C#: `var`, collection expressions, primary constructors, pattern matching, file-scoped namespaces.
- Braces always, opening brace on its own line, blank line before the final `return`, wrap near 120 columns.
- No single-use locals and no one-line private helpers. Comment only what the code cannot say.
- Money is `decimal`, stored `DECIMAL(12,2)` (fees `DECIMAL(10,2)`). Times are UTC `DateTime`.
- Phones go through `PhoneNumber.Parse` and are stored E.164 (`+8801XXXXXXXXX`).

## User-facing text

- Sentence case everywhere ("My orders", never "My Orders"). "and", never "&".
- Currency is ৳ with no decimals in the UI (`৳110`).
- Never show a raw id; show a name or the order number.

## Working in this repo

- The repo is CRLF (`.gitattributes`). Scope bulk `sed` edits to the files you mean.
- Demo data (logins, merchants, API keys) is Development-only, created by `DemoDataSeeder`.
