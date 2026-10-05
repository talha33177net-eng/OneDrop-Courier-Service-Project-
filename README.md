# Project

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="src/Web/wwwroot/images/logo-on-dark.svg" />
  <img src="src/Web/wwwroot/images/logo.svg" alt="OneDrop courier" width="260" />
</picture>

**OneDrop Courier: doorstep delivery and cash on delivery across Bangladesh.** A courier service for online shops, in
the way Steadfast, Pathao or RedX work: a merchant books parcels, a rider picks them up, the hubs sort them, a rider
delivers each one and collects the cash on delivery, and the merchant is paid that cash, less the courier's charges,
the next day.

.NET 10 · ASP.NET Core (Razor Pages + REST API) · EF Core 10 · SQL Server · Clean Architecture modular monolith.

| Who | What they do |
|---|---|
| Merchant | Signs up (approved by the courier), books parcels by form, spreadsheet or API, asks for pickups, prints labels, follows every parcel, checks a phone number's delivery record, sees its payments and invoices |
| Hub staff | Scan parcels in and out (receive, send to the delivery hub, hand back returns), send riders to pickups, hand parcels to riders, close each rider's run with the cash handed in |
| Rider | A phone screen: today's pickups and deliveries, and at each door delivered, partly delivered, on hold or refused |
| Courier admin | Live dashboard, merchants (approve, suspend, edit), riders, the rate card, coverage, payouts, failed messages |
| Recipient | Public tracking by code, and an SMS when the parcel goes out and when it is delivered |

Prices come from the courier's rate card, per service area (seeded values):

| Service area | Up to 1 kg | Each extra kg | COD charge | Extra for a return |
|---|---|---|---|---|
| Inside city | ৳60 | ৳15 | 1% | ৳0 |
| Suburb | ৳100 | ৳20 | 1% | ৳50 |
| Outside city | ৳120 | ৳20 | 1% | ৳60 |

## Documents

| Read | For |
|---|---|
| [Documentation/Project-Context.md](Documentation/Project-Context.md) | The product, every decision and why, the environment, traps already found. Start here |
| [Documentation/Architecture.md](Documentation/Architecture.md) | Diagrams: layers, the life of a parcel, parcel states, tenant isolation, the outbox, a day at the courier |
| [Documentation/Demo.md](Documentation/Demo.md) | The demo script, step by step |
| [Plans/Implementation-Plan.md](Plans/Implementation-Plan.md) | What is built, how it was tested, what is next, the decisions log and the daily log |
| [Documentation/Conventions.md](Documentation/Conventions.md), [Documentation/Database.md](Documentation/Database.md) | Rules for code and SQL |

## Run it

Prerequisites: .NET 10 SDK, SQL Server (the dev server is `10.50.0.1,1433` over the VPN), and SqlPackage
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
    "Database": "Server=10.50.0.1,1433;Database=OneDrop;User Id=...;Password=...;TrustServerCertificate=true;MultipleActiveResultSets=true"
  }
}
```

Then:

```powershell
./tools/db/publish.ps1                      # dbup pre -> publish the dacpac -> dbup (the courier, hubs, zones, areas, rates)
dotnet run --project src/Web                # seeds demo logins, merchants and riders on start
```

| Address | What |
|---|---|
| http://onedrop.localhost:5080 | OneDrop Courier: public site, tracking, sign-up, and every panel |
| http://localhost:5080 | The platform (lists couriers; platform admin only) |

Browsers resolve `*.localhost` to your machine, so no hosts-file changes are needed. In Development the app also books
about twenty sample parcels in every stage (`Seed:DemoActivity`), so every screen has something to show.

### Fill it with more parcels

With the app running, the simulator makes up shops and books their parcels through the API:

```powershell
dotnet run --project tools/Simulator -- --parcels 40              # 40 parcels from 6 made-up shops
dotnet run --project tools/Simulator -- --parcels 40 --pace 2     # one every 2 seconds: watch /Admin fill up
```

`--shops` (up to 8), `--seed`, `--tenant` (default: every courier in the database) and `--url` (default http://localhost:5080) are optional.
Each run gives each shop a new API key.

### Run it in Docker

No SQL Server or SDK needed, only Docker:

```powershell
copy .env.example .env          # then choose a SQL Server password in .env (git-ignored)
docker compose up --build       # SQL Server 2025, the database deployed from nothing, then the app
```

The same address works (http://onedrop.localhost:5080). SQL Server is on `localhost,14330`; `docker compose down -v`
deletes its data.

### Demo logins (Development only, password `OneDrop#2026`)

The sign-in page lists them as one-press buttons in Development.

| Login | Role |
|---|---|
| `admin@onedrop.test` | Courier admin |
| `hub@onedrop.test` | Hub staff (any hub; the hub is picked at the top of each hub page) |
| `fashion@onedrop.test`, `gadget@onedrop.test`, `beauty@onedrop.test`, `crafts@onedrop.test` | Merchants (Fashion House, Gadget BD, Beauty Shop, Chattogram Crafts) |
| `organic@onedrop.test` | Organic Bazar, a merchant still waiting for approval |
| `rider@onedrop.test`, `rider2@` (Mirpur), `rider3@` (Gulshan), `rider4@` (Chattogram), `rider5@` (Dhanmondi), `rider6@` (Uttara) | Riders |
| `admin@platform.test` on http://localhost:5080 | Platform admin |

The **Demo mode** bar links the fake gateways: **SMS inbox** (`/Dev/Sms`), **Payouts sent** (`/Dev/Payouts`) and
**Webhooks received** (`/Dev/Webhooks`).

### Demo API keys (Development only)

| Merchant | Key |
|---|---|
| Fashion House | `od_odfashion001_DevOnlyKeyDoNotUseInProduction01` |
| Gadget BD | `od_odgadget0001_DevOnlyKeyDoNotUseInProduction02` |
| Beauty Shop | `od_odbeauty0001_DevOnlyKeyDoNotUseInProduction03` |
| Chattogram Crafts | `od_odctgcraft01_DevOnlyKeyDoNotUseInProduction04` |

Merchants make and revoke their own keys on **API keys**.

## The merchant API

```bash
curl http://onedrop.localhost:5080/api/v1/parcels \
  -H "X-Api-Key: od_odfashion001_DevOnlyKeyDoNotUseInProduction01" \
  -H "Idempotency-Key: INV-1001" \
  -H "Content-Type: application/json" \
  -d '{
        "merchantReference": "INV-1001",
        "recipientName": "Rahim Uddin",
        "recipientPhone": "01712-345678",
        "recipientAddress": "House 12, Road 5",
        "area": "Gulshan 2",
        "codAmount": 1250,
        "weightKg": 1.5,
        "itemDescription": "Panjabi"
      }'
```

| Endpoint | |
|---|---|
| `POST /api/v1/parcels` | 201 with the tracking code (`OD10000001`), status, service area, delivery hub and charges. A retry with the same `Idempotency-Key` returns 200 and the same parcel; a different body with that key 409. A merchant waiting for approval gets 403 |
| `GET /api/v1/parcels/{code}` | The caller's own parcel and its history; anyone else's is 404 |
| `POST /api/v1/parcels/{code}/cancel` | Before pickup only (409 after); optional `reason` |
| `GET /api/v1/charge?area=&weightKg=&codAmount=` | What a parcel would cost: service area, delivery charge, COD charge, total, return charge |
| `GET /api/v1/areas` | The areas an address must pick from, with their city and hub |

### Status changes by webhook

A merchant sets a webhook address on **Webhooks** (`/Merchant/Webhook`, https only; plain http only to localhost) and
gets a `whsec_` secret there. Each status change of its parcels is posted through the outbox:

```json
{ "type": "parcel.status_changed", "timestamp": "2026-10-04T05:38:12Z",
  "data": { "trackingCode": "OD10000008", "merchantReference": "INV-2007", "status": "outForDelivery",
            "codAmount": 1250, "collectedAmount": null, "deliveryCharge": 60, "reason": null } }
```

Signed as [Standard Webhooks](https://www.standardwebhooks.com/): headers `webhook-id` (the same on a retry),
`webhook-timestamp` and `webhook-signature` = `v1,` + base64 HMAC-SHA256 of `{id}.{timestamp}.{body}` keyed with the
base64-decoded secret. Any 2xx answer counts; otherwise it is retried after 1, 2, 4 and 8 minutes, then given up
(the admin can send it again from **Failed messages**).

Background jobs run on Hangfire in the web app (the hourly merchant payouts); the dashboard is at
http://localhost:5080/jobs (platform admin).

## Layout

```
src/
  Domain            entities and business rules, no packages (Parcels, Pricing, Delivery, Payments, Merchants, Network)
  Application       one folder per use case (Parcels/CreateParcel, Hubs/HubScan, Delivery/RiderDay, Payments/RunPayouts, ...)
  Infrastructure    EF Core mapping, tenancy, Identity, Hangfire, fake SMS and payout gateways, webhooks, demo seeder
  Web               Razor Pages panels, /api/v1, SignalR, middleware, composition root
  Database          SQL project: owns the schema, builds a dacpac
  Database Update   DbUp data migrations (dbup.exe)
tests/
  Domain.Tests          business rules
  Architecture.Tests    every entity has a TenantId; the SQL project lists every .sql file
  Integration.Tests     real database: API, panels, the delivery flow, payouts, webhooks, isolation sweep, Row-Level Security
tools/
  db/publish.ps1        deploy the database (Windows PowerShell and PowerShell 7 on Linux)
  Simulator             demo shops and parcels through the running app's API
Dockerfile, docker-compose.yml, .github/workflows/ci.yml
Documentation/          context, architecture diagrams, demo, conventions
Plans/                  the plan and daily log
```

## Database

The schema is owned by the SQL project, **not** by EF migrations — the same setup as DCN. A new table or column goes
into `src/Database/<Schema>/Tables/<Table>.sql` first, then into the EF mapping in
`src/Infrastructure/Persistence/Configurations`. Data changes go into DbUp scripts. The integration test
`SchemaMatchesModelTests` fails if the two disagree. Full rules: [Documentation/Database.md](Documentation/Database.md).

## Keeping couriers and merchants apart

The system is multi-tenant: a tenant is a courier operator with its own hubs, riders, merchants and rates. One
courier is seeded; the integration tests add a second ("rival") to prove the walls hold.

1. **EF Core query filters** — every tenant-owned entity gets `WHERE TenantId = @current`, and merchant-owned ones
   also `WHERE MerchantId = @merchant` for merchant callers. No tenant set means no rows.
2. **Save interceptor** — stamps `TenantId` on new rows and refuses writes to another tenant's rows.
3. **SQL Server Row-Level Security** — the policy `Platform.TenantIsolation` filters every table with a `TenantId` by
   the tenant each application connection names in `SESSION_CONTEXT`, and refuses rows written for another tenant.

The tenant comes from the subdomain (panels) or the API key (merchant API). A merchant sees only its own parcels, and
a rider only the parcels given to them.

## Tests

```powershell
dotnet test --project tests/Domain.Tests
dotnet test --project tests/Architecture.Tests

./tools/db/publish.ps1 -Database OneDrop-Test   # after any schema change: keeps the test database current
dotnet test --project tests/Integration.Tests    # uses OneDrop-Test from testsettings.Local.json
```

The integration tests use their own database, `OneDrop-Test`, so their parcels never reach the development database
`OneDrop`. Set `INTEGRATION_TEST_DB` to point them somewhere else (CI, another developer's database).

**CI** (`.github/workflows/ci.yml`): builds, starts a throwaway SQL Server 2025 with a password made up for the run,
deploys the database into it from nothing with `publish.ps1`, runs the three suites (failing if the integration tests
were skipped) and builds the Docker images.
