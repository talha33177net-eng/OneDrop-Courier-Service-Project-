# Project

**Multiple shops. One delivery.** OneDrop groups orders from different online shops going to the same customer
into one delivery. Multi-tenant: each operator (OneDrop Dhaka, OneDrop Chattogram, ...) has its own customers,
hubs, merchants and prices.

.NET 10 · ASP.NET Core (Razor Pages + REST API) · EF Core 10 · SQL Server · Clean Architecture modular monolith.

## Status

New here? Start with [Documentation/Project-Context.md](Documentation/Project-Context.md). The detailed task list,
progress and daily log live in [Plans/Implementation-Plan.md](Plans/Implementation-Plan.md).

| Week | Scope | State |
|---|---|---|
| 1 — Foundation | Solution, tenancy (catalog, resolvers, filters, save guard), domain + database, Identity with roles and phone OTP, seeded tenants/zones/hubs, merchant API key + Create Order | **Done** |
| 2 — Grouping core | Customer matching, delivery groups + 3-day rule, quote (৳60 / +৳25), lock job + Ship now, outbox + fake SMS, customer group page | **Done** |
| 3 — Operations & money | Pickup routes + QR labels, hub scan/shelves/shuttle, rider trips, door payment, ledger + settlement | In progress (3.1 done) |
| 4 — Polish & proof | SignalR dashboards, webhooks, Row-Level Security, Docker, CI, simulator | |

## Run it

Prerequisites: .NET 10 SDK, SQL Server (the dev server is `ras-x2`), and SqlPackage
(`dotnet tool install -g microsoft.sqlpackage`).

**One-time setup: connection strings.** They contain the database password, so they are kept in git-ignored
files that you create on each machine:

| File | Database |
|---|---|
| `src/Web/appsettings.Local.json` | Development database (`OneDrop`) — used by the app and by `publish.ps1` |
| `tests/Integration.Tests/testsettings.Local.json` | Test database (`OneDrop-Test`) — used by the integration tests |

Both have the same shape:

```json
{
  "ConnectionStrings": {
    "Database": "Server=ras-x2,1433;Database=OneDrop;User Id=...;Password=...;TrustServerCertificate=true;MultipleActiveResultSets=true"
  }
}
```

Then:

```powershell
./tools/db/publish.ps1                      # dbup pre -> publish the dacpac -> dbup (tenants, zones, hubs, areas)
dotnet run --project src/Web                # http://localhost:5080 — seeds demo logins and merchants on start
```

Tenants are subdomains. Browsers resolve `*.localhost` to your machine, so no hosts-file changes are needed:

| Address | What |
|---|---|
| http://localhost:5080 | Platform (OneDrop company) |
| http://dhaka.localhost:5080 | OneDrop Dhaka — 7 zones, 5 hubs, ৳60 + ৳25 |
| http://chattogram.localhost:5080 | OneDrop Chattogram — 5 zones, 2 hubs, ৳70 + ৳30 |

### Demo logins (Development only, password `OneDrop#2026`)

| Login | Where | Role |
|---|---|---|
| `admin@onedrop.test` | localhost | Platform admin |
| `admin@dhaka.onedrop.test` / `admin@chattogram.onedrop.test` | tenant | Tenant admin |
| `hub@dhaka.onedrop.test` / `hub@chattogram.onedrop.test` | tenant | Hub staff (**Pickup routes**: route sheets per zone; **Scan**: collect, receive and load the shuttle; **Shelves**; **Shuttle** manifest; **Trips**: today's riders and deliveries, Plan trips now) |
| `rider@dhaka.onedrop.test` (Mirpur, 30 parcels / 25 kg), `rider2@dhaka.onedrop.test` (Mirpur, 12 / 15 kg), `rider3@dhaka.onedrop.test` (Gulshan), `rider@chattogram.onedrop.test` (Agrabad) | tenant | Rider (**Today**: stops, what to collect, Start trip) |
| `fashion@`, `gadget@`, `beauty@` + `dhaka.onedrop.test` / `chattogram.onedrop.test` | tenant | Merchant (orders, printable QR labels) |
| Any mobile number via **Customer sign in** | tenant | Customer (code appears on **SMS outbox**) |

### Demo merchant API keys (Development only)

| Tenant | Merchant | Key |
|---|---|---|
| Dhaka | Fashion House | `od_dhkfashion01_DevOnlyKeyDoNotUseInProduction01` |
| Dhaka | Gadget BD | `od_dhkgadget001_DevOnlyKeyDoNotUseInProduction02` |
| Dhaka | Beauty Shop | `od_dhkbeauty001_DevOnlyKeyDoNotUseInProduction03` |
| Chattogram | Fashion House | `od_ctgfashion01_DevOnlyKeyDoNotUseInProduction04` |
| Chattogram | Gadget BD | `od_ctggadget001_DevOnlyKeyDoNotUseInProduction05` |
| Chattogram | Beauty Shop | `od_ctgbeauty001_DevOnlyKeyDoNotUseInProduction06` |

### Create an order

```bash
curl http://localhost:5080/api/v1/orders \
  -H "X-Api-Key: od_dhkfashion01_DevOnlyKeyDoNotUseInProduction01" \
  -H "Idempotency-Key: fh-A102" \
  -H "Content-Type: application/json" \
  -d '{
        "externalReference": "A102",
        "customer": { "name": "Rahim Uddin", "phone": "01712-345678" },
        "address":  { "area": "Mirpur 10", "line1": "House 12, Road 5", "line2": "Flat 3B" },
        "packages": [ { "description": "T-shirt", "weightGrams": 400 } ],
        "codAmount": 800,
        "speed": "combine"
      }'
```

| Endpoint | |
|---|---|
| `POST /api/v1/orders` | 201 with the order number; a retry with the same `Idempotency-Key` returns 200 and the same order, a different body with that key 409 |
| `GET /api/v1/orders/{number}` | The caller's own order; anyone else's is 404 |
| `GET /api/v1/areas` | The area list an address must pick from |
| `GET /api/v1/quote?phone=&area=&line1=` | The delivery fee for the checkout: `{ fee, currency, joinsDelivery }` (৳60 for a new delivery, +৳25 when one is already on its way) |

Background jobs run on Hangfire in the web app; the dashboard is at http://localhost:5080/jobs (platform admin).

## Layout

```
src/
  Domain            entities and business rules, no packages
  Application       one folder per use case (Orders/CreateOrder, Auth/PhoneLogin, ...)
  Infrastructure    EF Core mapping, tenancy, Identity, SMS adapter, demo seeder
  Web               Razor Pages portals, /api/v1, middleware, composition root
  Database          SQL project: owns the schema, builds a dacpac
  Database Update   DbUp data migrations (dbup.exe)
tests/
  Domain.Tests          business rules
  Architecture.Tests    every entity has a TenantId; the SQL project lists every .sql file
  Integration.Tests     real database: schema matches EF, API isolation, save guard
tools/db/publish.ps1        deploy the database
```

## Database

The schema is owned by the SQL project, **not** by EF migrations — the same setup as DCN. A new table or
column goes into `src/Database/<Schema>/Tables/<Table>.sql` first, then into the EF mapping in
`src/Infrastructure/Persistence/Configurations`. Data changes go into DbUp scripts. The integration
test `SchemaMatchesModelTests` fails if the two disagree. Full rules: [Documentation/Database.md](Documentation/Database.md).

## Keeping tenants apart

1. **EF Core query filters** — every tenant-owned entity gets `WHERE TenantId = @current`, and merchant-owned
   ones also `WHERE MerchantId = @merchant` for merchant callers. No tenant set means no rows.
2. **Save interceptor** — stamps `TenantId` on new rows and refuses writes to another tenant's rows.
3. **SQL Server Row-Level Security** — Week 4.

The tenant comes from the subdomain (portals) or the API key (merchant API).

## Tests

```powershell
dotnet test --project tests/Domain.Tests
dotnet test --project tests/Architecture.Tests

./tools/db/publish.ps1 -Database OneDrop-Test   # after any schema change: keeps the test database current
dotnet test --project tests/Integration.Tests    # uses OneDrop-Test from testsettings.json
```

The integration tests use their own database, `OneDrop-Test`, so their throwaway orders and customers never
reach the development database `OneDrop`. Set `INTEGRATION_TEST_DB` to point them somewhere else (CI, another
developer's database).
