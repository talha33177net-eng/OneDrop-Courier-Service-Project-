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
| 3 — Operations & money | Pickup routes + QR labels, hub scan/shelves/shuttle, rider trips, market pricing (joinable fast deliveries, Ship now as an upgrade, weight allowance, staggered pickups), door payment, confirmation and advance payment, ledger + settlement, trust (advance after a refusal until 3 good deliveries; late shops drop off at the hub) | **Done** |
| 4 — Polish & proof | SignalR dashboards, webhooks, Row-Level Security, Docker, CI, simulator, combining deliveries to two spellings of one address, the shopping window | In progress (4.1–4.8 done) |

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
| http://dhaka.localhost:5080 | OneDrop Dhaka — 7 zones, 5 hubs, ৳60 + ৳25, fast ৳70 |
| http://chattogram.localhost:5080 | OneDrop Chattogram — 5 zones, 2 hubs, ৳70 + ৳30, fast ৳80 |

### Fill it with demo orders

With the app running, the simulator makes up shops for each operator and sends their orders through the API, from
customers who buy at several shops, so deliveries group as they would in real life:

```powershell
dotnet run --project tools/Simulator -- --orders 40               # both operators, 40 orders each
dotnet run --project tools/Simulator -- --tenant dhaka --pace 2   # one order every 2 seconds: watch /Admin fill up
```

`--shops` (up to 8), `--seed` (the same customers again) and `--url` (default http://localhost:5080) are optional. It
uses the app's database for the shops (the same connection string as the app) and gives each a new API key per run.

### Run it in Docker

No SQL Server or SDK needed, only Docker:

```powershell
copy .env.example .env          # then choose a SQL Server password in .env (git-ignored)
docker compose up --build       # SQL Server 2025, the database deployed from nothing, then the app
```

The same addresses as above work (http://dhaka.localhost:5080). The `database` service runs `publish.ps1` against the
container's SQL Server and exits; the app starts once it has succeeded. SQL Server is on `localhost,14330` for
your own queries; `docker compose down -v` deletes its data.

### Demo logins (Development only, password `OneDrop#2026`)

| Login | Where | Role |
|---|---|---|
| `admin@onedrop.test` | localhost | Platform admin |
| `admin@dhaka.onedrop.test` / `admin@chattogram.onedrop.test` | tenant | Tenant admin (**Dashboard**: every hub live and packages per delivery by area, week by week; also every hub page) |
| `hub@dhaka.onedrop.test` / `hub@chattogram.onedrop.test` | tenant | Hub staff (**Hub today**: live counts and the parcels of today's deliveries not scanned in yet; **Pickup routes**: route sheets per zone; **Scan**: collect, receive, load the shuttle and return to the shop; **Shelves**; **Shuttle** manifest; **Trips**: today's riders and deliveries, Plan trips now; **Cash**: record each rider's cash handed in) |
| `rider@dhaka.onedrop.test` (Mirpur, 30 parcels / 25 kg), `rider2@dhaka.onedrop.test` (Mirpur, 12 / 15 kg), `rider3@dhaka.onedrop.test` (Gulshan), `rider@chattogram.onedrop.test` (Agrabad) | tenant | Rider (**Today**: stops, what to collect, Start trip; at each door: refused orders, check the amount, hand over for cash or after a bKash / Nagad QR is paid (the fake wallet is paid on **Wallet payments**), or nobody home; cash to hand in) |
| `fashion@`, `gadget@`, `beauty@` + `dhaka.onedrop.test` / `chattogram.onedrop.test` | tenant | Merchant (orders, printable QR labels; **Payouts**: COD owed, charges, payouts sent the next day, listed on **Wallet payments**) |
| Any mobile number via **Customer sign in** | tenant | Customer (code appears on **SMS outbox**; a first cash order is confirmed, or its fee paid in advance, through the link in its text) |

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
| `POST /api/v1/orders` | 201 with the order number and `fee`; `waitsFor` says what the order waits for from the customer (`none`, `confirm`, or `payInAdvance` when the shop sends `"feeInAdvance": true`, after a refusal or a no-show, until the customer has accepted the operator's count of deliveries since). A retry with the same `Idempotency-Key` returns 200 and the same order, a different body with that key 409 |
| `GET /api/v1/orders/{number}` | The caller's own order; anyone else's is 404 |
| `GET /api/v1/areas` | The area list an address must pick from |
| `GET /api/v1/quote?phone=&area=&line1=` | The delivery fee for the checkout: `{ fee, currency, joinsDelivery }` (৳60 for a new delivery, +৳25 when one is already on its way, the fast fee for `speed=fast`). Optional `weightGrams` adds each started kg above the shop's allowance (Dhaka 2 kg, then ৳15); optional `pickupPointId` (default: the shop's default point) |

### Order updates by webhook

A shop sets its webhook address on **Order updates** (`/Merchant/Webhook`, https only; plain http only to localhost)
and gets a `whsec_` secret there. Each status change of its orders (`pickedUp`, `atHub`, `outForDelivery`,
`delivered`, `refused`, `returnedToMerchant`, `cancelled`) is posted through the outbox:

```json
{ "type": "order.status_changed", "timestamp": "2026-09-30T05:38:12Z",
  "data": { "number": "OD-100108", "externalReference": "FB-2001", "status": "atHub" } }
```

Signed as [Standard Webhooks](https://www.standardwebhooks.com/): headers `webhook-id` (the same on a retry),
`webhook-timestamp` and `webhook-signature` = `v1,` + base64 HMAC-SHA256 of `{id}.{timestamp}.{body}` keyed with the
base64-decoded secret. Any 2xx answer counts; otherwise it is retried after 1, 2, 4 and 8 minutes. The body never
names the delivery or the customer's other shops. In Development, set the address to
`http://localhost:5080/Dev/Webhooks` to see what arrives and whether its signature checks.

### Shopping window

A shop that wants new customers lists itself on **Shop window** (`/Merchant/Window`): the address customers shop at
(website or Facebook page) and a line on what it sells. Customers whose delivery is still open to other shops see the
listed shops: the "joined" SMS ends with "Add from any OneDrop shop for +৳25:" and a link to the operator's public
**Shops** page (`/Shops`, the same list for everyone), and each open delivery on "My deliveries" lists the shops not
already in it. Shops are never shown who bought where.

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
3. **SQL Server Row-Level Security** — the policy `Platform.TenantIsolation` filters every table with a `TenantId`
   by the tenant each application connection names in `SESSION_CONTEXT`, and refuses rows written for another
   tenant, so a lifted filter or raw SQL still stays inside the tenant.

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

**CI** (`.github/workflows/ci.yml`, on pushes to `main` and `day*` and on pull requests): builds, starts a throwaway
SQL Server 2025 with a password made up for the run, deploys the database into it from nothing with `publish.ps1`,
runs the three suites (failing if the integration tests were skipped) and builds the Docker images.
