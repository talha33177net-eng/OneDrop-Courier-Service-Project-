# Project context — read this first

Everything someone new to this project needs to continue the work without asking: what we are building and why,
what exists, every decision taken and the reason for it, the environment, and the traps already found.

**Reading order for a new session:**
1. This file.
2. [Plans/Implementation-Plan.md](../Plans/Implementation-Plan.md) — the "Today" section says where we stopped and
   what is next; the daily log says what was done and tested.
3. [Conventions.md](Conventions.md) and [Database.md](Database.md) — the rules code and SQL must follow.

Keep this file current: when a decision, a working rule or the environment changes, update it in the same change.

---

## 1. The product in one page

**OneDrop — "Multiple shops. One delivery."** In Bangladesh, buying from three online shops means three couriers,
three trips and three delivery charges (৳60 each inside Dhaka). OneDrop combines orders from different shops going
to the same customer into **one delivery**.

- **Customer** picks OneDrop at the shop's checkout. Pays ৳60 for the first shop and **৳25 for each extra shop**
  in the same delivery (3 shops = ৳110 instead of ৳180). The fee is paid **at the door on delivery day** (cash or
  bKash/Nagad QR), because only then is the group complete. No fee, no handover.
- **Merchant** pays nothing. Sends orders by API (websites) or by the dashboard (Facebook sellers). Parcels are
  collected on scheduled zone pickup routes. Cash on delivery (COD) is settled to the merchant the next day.
- **OneDrop** earns the delivery fee; the margin grows with every extra shop in a delivery. The key number to grow
  is **packages per delivery (target 2+)**. The biggest business risk is low density (too few orders per area).

### How grouping works (the core rules)
- Orders are matched by **phone number + delivery address** (so home and office are separate groups). Same
  phone → same customer; the phone is normalised to E.164.
- A **delivery group** opens with the customer's first order. Orders placed on **Day 1 and Day 2** join it; it
  **locks at the end of Day 2** (11:59 PM, tenant time zone) and is delivered on **Day 3** (5–9 PM). The deadline is
  counted from the first order and **never moves**. An order on Day 3 starts a **new** group.
- **Ship now:** the customer can close the group early (app, or SMS "reply 1").
- **Deliver fast:** next-day delivery for ৳60, no waiting. **Don't hold:** merchants flag food, medicine or dated
  gifts to skip waiting.
- Fee is always calculated on what is **actually delivered** and counts **distinct accepted shops** only.
- Only **one open group per customer + address**, even when two orders arrive at the same moment.

### Operations
Scheduled pickup route per zone (e.g. 2 PM Mirpur) → parcels scanned into the **hub** → each open group gets a
labelled **shelf** → parcels from other zones come by a daily **hub shuttle** → on Day 3 one **rider** delivers
the whole group and collects fee + COD → next day each merchant is **settled**.

Operating rules from the documentation: merchant not ready at pickup → group leaves without it, customer still
pays only +৳25; customer refuses one parcel → fee counts accepted shops only, parcel returns; customer not home →
one free re-attempt, then return and trust score drops; low **trust score** (refusals, no-shows) → pay the fee in
advance by payment link; merchants never see that the customer also bought elsewhere; scan at every handover.

### Multi-tenancy
A **tenant is an operator** (OneDrop Dhaka, OneDrop Chattogram, a partner courier). Each has its own customers,
hubs, riders, merchants and prices, and never sees another tenant's data. **Inside** a tenant, the system sees all
merchants (grouping needs it) but **each merchant sees only its own orders**. That is why a merchant is not a tenant.

### Source documents
The user supplied two PDFs (not stored in the repo): *OneDrop Implementation Plan* (4-week technical plan) and
*OneDrop Project Documentation* (product, pricing, rules). Their content is captured here and in the plan.

---

## 2. The 4-week MVP plan and where we are

| Week | Theme | Done when | Status |
|---|---|---|---|
| 1 | Foundation | An order can be created for a tenant | ✅ Done 2026-09-27 |
| 2 | Grouping core | 3 shops' orders form 1 group | 🟡 In progress — 2.1–2.2 done, next 2.3 |
| 3 | Operations and money | Group delivered, merchants settled | ⬜ |
| 4 | Polish and proof | Full demo runs end to end | ⬜ |

Task-level detail, the cut list, the job schedule, must-pass tests and the daily log are in
[Plans/Implementation-Plan.md](../Plans/Implementation-Plan.md). **That file is the source of truth for progress.**

---

## 3. What exists today (Week 1, plus Week 2 tasks 2.1–2.2)

### Solution layout (`Courier.sln`)
| Project | Path | Contents |
|---|---|---|
| Domain | `src/Domain` | Entities and rules, no packages. `Common` (Entity, TenantEntity, Result/Error), `Platform/Tenant`, `Network` (Hub, Zone, Area), `Customers` (Customer, CustomerAddress, PhoneNumber, PhoneOtp), `Merchants` (Merchant, MerchantApiKey, PickupPoint), `Orders` (Order + state machine, Package, OrderStatusHistory), `Grouping` (DeliveryGroup + state machine and lock time) |
| Application | `src/Application` | Use cases as vertical slices: `Orders/CreateOrder`, `Orders/GetOrder`, `Network/ListAreas`, `Auth/PhoneLogin`, `Customers/CustomerDirectory` (find-or-create by phone/address), `Grouping/DeliveryGrouping` (join or open the order's group). Interfaces: `IAppDbContext`, `ITenantContext`, `ITenantCatalog`, `ICurrentUser`, `ISmsSender` |
| Infrastructure | `src/Infrastructure` | `Persistence/AppDbContext` (EF Core, query filters), `Configurations/*` (mapping), `TenantSaveInterceptor`, `MultiTenancy` (TenantContext, TenantCatalog), `Identity` (AppUser, AppRole, claims), `Sms/FakeSmsSender`, `Seeding/DemoDataSeeder` |
| Web | `src/Web` | Razor Pages portals, `Api/V1` (orders, areas), `Authentication/ApiKeyAuthenticationHandler`, `MultiTenancy` middleware, `Program.cs` |
| Database | `src/Database` | SQL project (Microsoft.Build.Sql 2.1.0) → `Database.dacpac`. Owns the schema |
| Database Update | `src/Database Update` | DbUp console (`dbup.exe`): data migrations in `Scripts/<Year>/`, data-loss scripts in `Scripts/Pre/` |
| Tests | `tests/Domain.Tests`, `tests/Architecture.Tests`, `tests/Integration.Tests` | 70 + 6 + 26 = **102 tests, all passing** |
| Tools | `tools/db/publish.ps1` | Deploys a database: `dbup pre` → dacpac publish → `dbup` |

### Features that work
- `POST /api/v1/orders` (API key in `X-Api-Key`, optional `Idempotency-Key`): validates per field, finds or
  creates the customer by phone and the address by match key, resolves area → zone → hub, saves order + packages
  + first status in one save. Same key + same body → 200 with the same order; same key + other body → 409.
- **Grouping on create** (`Application/Grouping/DeliveryGrouping`): in the same save the order joins the
  customer's open group for that address or opens one (window = the tenant's `GroupJoinDays` in its time zone).
  Deliver fast and Don't hold orders get their own group, locked at once for next-day delivery. An open group
  past its deadline is locked on the spot. Two orders opening the same group at once: the unique index refuses
  one, which then joins the winner's group. The response never shows the group (merchant privacy).
- `GET /api/v1/orders/{number}` (own orders only; others 404), `GET /api/v1/areas`.
- Staff login (email + password) per tenant subdomain; customer login by SMS code; the fake SMS sender shows codes
  at `/Dev/Sms` in Development.
- Pages: landing (platform or tenant), merchant order list + API key list, customer "My deliveries", platform
  tenant list (the only cross-tenant page).
- **Not yet:** fee in the order response (2.3), the lock job (2.5; until then a group locks when the next order
  arrives after its deadline), Ship now, jobs (Hangfire), merchant screens to create API keys or enter orders
  manually, tenant admin screens.

### Database
- Schemas: `Platform` (Tenant), `Identity` (User, Role, UserRole, UserClaim, UserLogin, UserToken, RoleClaim),
  `Network` (Hub, Zone, Area), `Customers` (Customer, CustomerAddress, PhoneOtp), `Merchants` (Merchant,
  MerchantApiKey, PickupPoint), `Orders` (Order, Package, OrderStatusHistory, sequence OrderNumber → `OD-100001`),
  `Grouping` (DeliveryGroup, sequence DeliveryGroupNumber → `DG-100001`; one `Open` group per customer + address
  by filtered unique index). `Order.DeliveryGroupId` is NOT NULL: every order travels in a group
  (`Scripts/Pre/001_GroupExistingOrders` grouped the orders saved before 2.2).
- `Platform.Tenant` settings (fees, `GroupJoinDays`, time zone, currency, SMS sender) have **no defaults**, in
  SQL or C#: every tenant states its own. No business value is hard-coded anywhere.
- Every tenant table: `TenantId` + FK + index; housekeeping columns `Archived`, `UpdatedId`, `UpdatedOn`, `Created`.
- Seeded by DbUp `2026/001_SeedLaunchTenants.sql`: **OneDrop Dhaka** (id 1, slug `dhaka`, 7 zones on 5 hubs,
  32 areas, ৳60 + ৳25) and **OneDrop Chattogram** (id 2, slug `chattogram`, 5 zones on 2 hubs, 14 areas,
  ৳70 + ৳30). Roles are seeded by `Script.PostDeployment.sql`.
- Dev data in `OneDrop` right now: merchants 1–3 (Dhaka: Fashion House, Gadget BD, Beauty Shop) and 4–6
  (Chattogram, same names); orders OD-100001, OD-100002, OD-100004 (the three Dhaka shops, **same customer 1 and
  address 1, one group DG-100003**) and OD-100003 (Chattogram, same phone, different customer 2, DG-100004).
  OD-100005 onwards are the 2.2 live check (phone 01563583024: one Dhaka group of 10 orders, one fast order,
  one Chattogram order). Group numbers have gaps: a sequence value used in a rolled-back dry run is not reused.

---

## 4. Decisions and the reasons for them

| Decision | Why |
|---|---|
| Modular monolith, Clean Architecture, 4 projects | From the plan: fast to build, modules can be split out later |
| **Schema owned by a SQL project + DbUp, no EF migrations** | The user asked for the database "just like DCN" (their project at `C:\Git\DCN`) so it is easy to maintain. `SchemaMatchesModelTests` fails if EF and SQL drift |
| **No product-name prefix in code** (`src/Web`, `AppDbContext`, `Courier.sln`) | User preference, DCN style. "OneDrop" appears only in UI text, SMS text, tenant data, demo logins and the database names |
| `TenantId` on every row (not DCN's AccountId/LocationId) | The plan's design; named filters `Tenant` and `Merchant` in `AppDbContext` |
| Isolation layers: 1 EF query filters, 2 save interceptor, 3 Row-Level Security (Week 4) | From the plan: if one layer has a bug the next still stops a leak |
| Tenant from the **subdomain** (portals) or the **API key** (API); staff sign in on their own subdomain; the bare domain is platform staff only | Cookies are host-only, which keeps tenants apart. Resolving a tenant from the login alone is left for a future rider app |
| `Zone.HubId` — a zone is served by one hub, a hub serves several zones | 7 Dhaka zones on 3–5 hubs as the MVP scope requires (the PDF diagram drew Zone 1–* Hub) |
| Customer = phone per tenant; address matched by a normalised **match key** (`h 12 r 5 f 3 b`) | Same person across shops; typos and spellings of Dhaka addresses |
| API keys `od_{12-char prefix}_{32-char secret}`, only a SHA-256 hash stored | Prefix finds the key before the tenant is known; the secret never lives in the database |
| Idempotency key unique per merchant + request hash | Merchant retries never duplicate an order; a reused key with another body is a 409 |
| Order numbers from a SQL sequence (`OD-` + 100001…) | Unique across tenants, assigned by the database |
| Integration tests use a separate database `OneDrop-Test` | The must-pass tests need real SQL Server; test data never touches `OneDrop` |
| Secrets only in git-ignored `*.Local.json` files | The repository must never contain the database password |
| GitHub repo private | Chosen by the user; can be made public later for a portfolio |
| **No hard-coded business values**: prices, join days, time zone and the rest come from the tenant's settings | Owner rule (2026-09-28). Each operator has its own; a default would silently give a new one Dhaka's |
| Deliver fast / Don't hold → own group, `Locked` at once, `LocksAt` = next midnight | They never wait, so they must not take the customer's one `Open` slot |
| The merchant API never returns group data | A merchant must not learn the customer also bought elsewhere |

---

## 5. Environment

| Item | Value |
|---|---|
| Machine | Windows 11, .NET SDK 10.0.4xx, PowerShell 5.1 + Git Bash |
| SQL Server | `ras-x2,1433`, SQL Server 2025, login `sa`. Shared with the team (DCN-&lt;name&gt; databases) |
| Databases | `OneDrop` (development), `OneDrop-Test` (integration tests). **Ask before creating any other** |
| Connection strings | `src/Web/appsettings.Local.json` (dev; also read by `publish.ps1` and passed to DbUp) and `tests/Integration.Tests/testsettings.Local.json` (test). Both git-ignored; format in the README |
| Tools | `sqlpackage` (dotnet global tool), `sqlcmd`, GitHub CLI at `C:\Program Files\GitHub CLI\gh.exe` (signed in as `talha33177net-eng`) |
| Repository | https://github.com/talha33177net-eng/OneDrop (private), branch `main`, local folder `C:\Courier Project` |
| Git identity | Talha Ahmed &lt;talha33177.net@gmail.com&gt; |
| App URLs | http://localhost:5080 (platform), http://dhaka.localhost:5080, http://chattogram.localhost:5080 |
| Demo logins, API keys | [README](../README.md) — password `OneDrop#2026`, keys `od_dhkfashion01_…` etc. (Development only) |

### Everyday commands
```powershell
./tools/db/publish.ps1                           # update the dev database (after any schema or DbUp change)
./tools/db/publish.ps1 -Database OneDrop-Test    # update the test database (same trigger)
dotnet build Courier.sln
dotnet test --project tests/Domain.Tests
dotnet test --project tests/Architecture.Tests
dotnet test --project tests/Integration.Tests    # must report succeeded, not skipped
dotnet run --project src/Web
git push                                          # main tracks origin/main
```

---

## 6. Working rules agreed with the owner

1. **Test every piece of work right after it is implemented** — the full routine in the plan (build, new tests,
   republish databases if the schema changed, all three suites green, live check in the running app), then tick
   the task and write the daily log. Never report a task done without the results.
2. **Follow DCN** (`C:\Git\DCN`) for conventions unless the OneDrop plan says otherwise.
3. **No product-name prefix** in projects, folders, namespaces or class names.
4. **Commits are authored by the repository owner only.** No co-author trailers, no "generated with" lines, no
   tool-named files in the repository.
5. **Never commit secrets.** Connection strings stay in `*.Local.json`. Scan staged files before every push.
6. **Ask before creating databases** on the shared server, and before anything that is hard to undo.
7. Leave changes uncommitted for review unless asked to commit or push.
8. Keep this file and the plan up to date as part of the work.
9. **Never hard-code business values** (prices, join days, time zone, currency, sender name): they are tenant
   settings. No constants in code, no defaults in SQL; only the seed script and tests hold concrete numbers.

---

## 7. Traps already found (do not rediscover them)

| Trap | What to do |
|---|---|
| DbUp journals scripts by embedded resource name, which includes the project's root namespace (`DatabaseUpdate.Scripts._2026.001_…`) | Never rename the namespace or folders of `Database Update` without updating `dbo.SchemaVersions` in every database, or scripts re-run |
| `PRINT` without `;` before a `WITH` CTE in the post-deploy script fails the publish | End statements before a CTE with `;` |
| PowerShell 5.1 turns native stderr into terminating errors under `$ErrorActionPreference = 'Stop'` | Scripts use `Continue` and check `$LASTEXITCODE` |
| `WebApplicationFactory` starts its host lazily without a lock; parallel tests started several hosts whose seeders raced on an empty database | The test fixture starts the host once in `InitializeAsync` |
| The web app reads `appsettings.Local.json` (dev database); tests boot the web app | Tests override the connection with `ConfigureAppConfiguration` (added last), and `AddInfrastructure` reads the connection string lazily. Verified: tests only write to `OneDrop-Test` |
| xUnit v3 4.x on .NET 10 needs Microsoft.Testing.Platform | `global.json` opts in; run `dotnet test --project <path>` |
| An EF store-generated string with a non-null default (`""`) is sent to SQL and skips the sequence | `Order.Number` starts as `null!` |
| `DATETIME2(0)` rounds, so the saved entity and the row differed | The interceptor truncates `Created` to whole seconds |
| EF adds `WHERE [TenantId] IS NOT NULL` to unique indexes on nullable columns | `UX_User_Tenant_UserName` sets its filter explicitly so platform user names stay unique |
| The machine-wide NuGet config includes DCN's private Rasdan feed | Repo `nuget.config` clears sources and uses nuget.org only |
| `*.localhost` subdomains work in browsers; for curl use `--resolve dhaka.localhost:5080:127.0.0.1` or a `Host` header | — |
| Microsoft.Build.Sql 2.2.0 breaks builds inside Visual Studio | Stay on 2.1.0 (same as DCN) |
| SqlPackage blocks NULL → NOT NULL on any table with rows, even when no row is NULL | Backfill **and** `ALTER COLUMN` in a guarded `Scripts/Pre` script (drop the column's index and FK first; the publish recreates them) |
| `sqlcmd` runs with `QUOTED_IDENTIFIER OFF`; statements touching a filtered index fail | Pass `-I` when running scripts by hand (DbUp's connection already has it on) |
| Detaching an added principal that a tracked order still points at throws (required relationship severed) | Re-point the order (`PlaceIn` + `Entry(order).DetectChanges()`) before detaching |
| A running `dotnet run --project src/Web` locks `src/Web/bin`; builds then silently keep the old DLLs for tests | Stop the app before rebuilding or running the test suites |

---

## 8. Glossary

| Term | Meaning |
|---|---|
| Tenant | An operator (OneDrop Dhaka). Selected by subdomain or API key |
| Merchant | A shop. Sees only its own orders |
| Delivery group | All of one customer's orders to one address that travel together (Week 2) |
| Zone / Area / Hub | City part with its own pickup routes / neighbourhood picked from a list / local warehouse serving zones |
| Collection (pickup) route | Scheduled daily pickup visiting many merchants in one zone |
| Hub shuttle | Daily transfer of parcels between hubs to the customer's zone hub |
| COD | Cash on delivery: product money collected at the door, settled to the merchant next day |
| Trust score | Customer record of refusals and no-shows; low score = pay the fee first |
| Day 1 / 2 / 3 | First order day / last day to join / delivery day (tenant time zone) |
