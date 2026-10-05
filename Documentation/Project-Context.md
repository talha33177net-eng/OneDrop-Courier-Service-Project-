# Project context — read this first

Everything someone new to this project needs to continue the work without asking: what we are building and why,
what exists, every decision taken and the reason for it, the environment, and the traps already found.

**Reading order for a new session:**
1. This file.
2. [Plans/Implementation-Plan.md](../Plans/Implementation-Plan.md) — the "Today" section says where we stopped and
   what is next; the daily log says what was done and tested.
3. [Conventions.md](Conventions.md) and [Database.md](Database.md) — the rules code and SQL must follow.
4. [Architecture.md](Architecture.md) for diagrams of how the parts fit, and [Demo.md](Demo.md) for the demo.

Keep this file current: when a decision, a working rule or the environment changes, update it in the same change.

---

## 1. The product in one page

**OneDrop Courier** is a courier service for online shops in Bangladesh, built the way the country's couriers work
(Steadfast, Pathao, RedX, Paperfly): a shop books a parcel, a rider picks it up, the hubs sort it, a rider delivers it
and collects the cash on delivery (COD), and the shop is paid that cash, less the courier's charges, the next day.

> **The pivot (2026-10-04).** Until then the project was "OneDrop — multiple shops, one delivery": orders from several
> shops for one customer were grouped into one delivery over three days. The owner dropped that idea: "forget the
> group delivery system … make this project just like the traditional delivery systems of Bangladesh like Steadfast".
> All grouping logic, customer accounts, trips and shelves were removed, both databases were reset and the schema and
> seed were written anew. The last commit of the grouping product is `3b48bc9`; an uncommitted rewrite of its Week 5
> screens is kept as `git stash@{0}`. Its plan and daily log are in git history, not in the current plan.

### Who uses it
- **Merchant** (a shop: website, Facebook page): signs up on the public site and waits for the courier's approval;
  books parcels by form, CSV upload or API; asks for pickups; prints QR labels; follows every parcel; checks a phone
  number's record before sending COD (fraud check); sees what it is owed, its payouts and invoices; manages API keys
  and a webhook.
- **Hub staff**: one login for every hub, the hub picked on the page. Scan parcels in (receive), out to another hub
  (dispatch) and back to the merchant (hand back); assign pickups and parcels to riders; close each rider's run with
  the cash handed in.
- **Rider**: a phone screen (installable) with today's pickups and deliveries; records each door as delivered, partly
  delivered, on hold (reason and a date) or refused.
- **Courier admin**: live dashboard, merchants (approve, suspend, edit, payout account), riders, the rate card,
  coverage (hubs, zones, areas), payouts (run now, invoices), failed messages.
- **Recipient**: no account. Public tracking by code, and an SMS when the parcel goes out and when it is delivered.

### Pricing (the courier's rate card, seeded)
| Service area | Up to 1 kg | Each started kg above | COD charge | Extra for a return |
|---|---|---|---|---|
| Inside city | ৳60 | ৳15 | 1% | ৳0 |
| Suburb | ৳100 | ৳20 | 1% | ৳50 |
| Outside city | ৳120 | ৳20 | 1% | ৳60 |

The service area comes from the pickup point's zone and the destination's zone (`ServiceAreas.Between`): same city
and neither a suburb → inside city; same city with a suburb zone → suburb; different cities → outside city. The COD
charge is a percentage of the COD amount rounded to whole taka (away from zero). A returned parcel costs the delivery
charge plus the return charge; a cancelled one costs nothing; a delivered or partly delivered one costs the delivery
charge plus the COD charge on what was collected.

### The life of a parcel
`Pending` (booked) → `PickedUp` (rider collected) → `AtHub` (received) → `InTransit` (sent to the delivery hub) →
`AtHub` → `OutForDelivery` (in a rider's run) → `Delivered` / `PartlyDelivered` / `OnHold` / `Returning` → `Returned`
(handed back at the pickup hub). `Cancelled` only before pickup. Where it is lives on the parcel: `CurrentHubId`,
`TransferToHubId` or `RiderId`, at most one (SQL check). Every move writes a `ParcelEvent` (status, note, hub).
On hold is allowed while attempts remain (`Tenant.MaxDeliveryAttempts`, 3); the last failed attempt returns it.

### Money
Each finished parcel writes its ledger lines in the same save (`LedgerEntry.For`): `Cod` (+ collected),
`DeliveryCharge` (−), `CodCharge` (−), `ReturnCharge` (−); ৳0 lines are skipped. The hourly `merchant-payouts` job pays
each merchant every unpaid line up to yesterday (courier's time zone) as a `Payout` (`INV-000001`) through the payout
gateway (fake in Development, listed on `/Dev/Payouts`) with the key `{slug}-payout-{id}`; a merchant whose lines
come to ৳0 or less, or with no payout account, waits. The rider's cash is checked when hub staff close the run
(`CashExpected` vs `CashReceived`); payouts do not wait for it.

### Multi-tenancy
A **tenant is a courier operator** with its own hubs, zones, areas, rates, riders and merchants. One courier is seeded
(OneDrop Courier, slug `onedrop`); the integration tests add a second ("rival", in `OneDrop-Test` only) to prove the
isolation. Inside a courier each merchant sees only its own parcels and each rider only the parcels given to them.

---

## 2. Where we are

The traditional courier rebuild is done on branch `traditional-courier` (uncommitted, for the owner's review). All
three test suites pass; the app was checked live on the dev database. The plan's "Today" says what is next.

---

## 3. What exists today

### Solution layout (`Courier.sln`)
| Project | Path | Contents |
|---|---|---|
| Domain | `src/Domain` | `Common` (Entity with domain events, TenantEntity, Result/Error, PhoneNumber), `Platform/Tenant`, `Network` (Hub, Zone with `City` and `IsSuburb`, Area), `Pricing` (ServiceArea, DeliveryRate, ParcelCharges), `Merchants` (Merchant with status, payout account, webhook; MerchantApiKey; PickupPoint; WebhookSignature), `Parcels` (Parcel and its state machine, ParcelEvent, ParcelStatusChanged), `Delivery` (Rider, DeliveryRun, DeliveryAttempt, PickupRequest), `Payments` (LedgerEntry, Payout), `Notifications` (OutboxMessage) |
| Application | `src/Application` | Use cases as vertical slices: `Parcels` (CreateParcel, Browse, ParcelActions, Labels, Track, Quote, FraudCheck, BulkImport), `Hubs` (HubDirectory, HubScan, HubBoard, AssignParcels, Runs), `Delivery` (Pickups, RiderDay, Riders), `Merchants` (Onboarding, Admin, Account, ApiKeys, Webhook), `Payments` (RunPayouts/PayoutsJob, MerchantPayments, AdminPayouts), `Pricing/Rates`, `Network` (ListAreas, Coverage), `Dashboards`, `Notifications` (outbox contracts, RecipientTexts + SendOutboxJob, SendWebhooksJob, FailedMessages). Interfaces: `IAppDbContext`, `ITenantContext`, `ITenantCatalog`, `ICurrentUser`, `ISmsSender`, `IWebhookSender`, `IPayoutGateway`, `ITrackingLinks`, `IUserAccounts`, `IOperationsFeed`, `ITenantJob` |
| Infrastructure | `src/Infrastructure` | `Persistence/AppDbContext` (query filters, outbox writer, `AcrossTenantsAsync`), `Configurations/*`, `TenantSaveInterceptor`, `TenantSessionInterceptor`, `MultiTenancy`, `Identity` (AppUser, UserAccounts), `Sms/FakeSmsSender`, `Payments/FakePayoutGateway`, `Webhooks/HttpWebhookSender`, `Seeding` (DemoDataSeeder, DemoActivity), `Jobs` (Hangfire, TenantJobRunner, TenantJobRegistry, OutboxDispatcher) |
| Web | `src/Web` | Razor Pages: public site (`/`, `/Track`, `/Coverage`, `/Account/*`), `Merchant/*`, `Hub/*`, `Rider/*`, `Admin/*`, `Platform/Tenants`, `Dev/*`; `Api/V1` (parcels, charge, areas); `Live` (SignalR); `Display` (`Icons`, `Statuses`, `Money`); `wwwroot/css/site.css` (the design system, with its motion), `wwwroot/css/landing.css` (the front page's drawings) and `wwwroot/js/site.js` (shared behaviours: count-up, busy buttons, confirm dialog, toasts, tips, "/" search, drawings resting off screen, sections rising in); `Pages/Shared/Art/*` (the front page's line drawings), `Pages/Shared/_BrandMark.cshtml` (the logo); `wwwroot/images/logo.svg` and `logo-on-dark.svg` (the logo with its name, outlined), `wwwroot/icons` (app icons) |
| Database | `src/Database` | SQL project (Microsoft.Build.Sql 2.1.0) → `Database.dacpac`. Owns the schema |
| Database Update | `src/Database Update` | DbUp: `Scripts/2026/001_SeedCourier.sql` (the courier, 16 hubs, 21 zones, 87 areas, the rate card); `Scripts/Pre` empty |
| Tests | `tests/Domain.Tests`, `tests/Architecture.Tests`, `tests/Integration.Tests` | 111 + 6 + 57 = **174 tests, all passing** |
| Tools | `tools/db/publish.ps1`, `tools/Simulator` | Deploy a database; book made-up shops' parcels through the running app's API (`--parcels 40 --pace 2`) |
| Docker and CI | `Dockerfile`, `docker-compose.yml`, `.github/workflows/ci.yml` | Compose = SQL Server 2025 + deploy + app on `onedrop.localhost:5080`; CI deploys a throwaway SQL Server from nothing and runs every suite |

### Features
- **Booking.** `CreateParcelHandler` serves the API, the form (`/Merchant/NewParcel`, a live quote beside it) and the
  CSV upload (`/Merchant/BulkUpload`: every row booked in one transaction or none, with the rows to fix; a template to
  download). Area by id or name; the pickup point defaults to the merchant's default; the phone is normalised to E.164;
  charges are snapshot from the rate card. Idempotency per merchant and request hash (same key + same body → 200 with
  the same parcel, other body → 409). A merchant not `Active` cannot book (403 `parcel.merchantNotActive`).
- **Merchant panel.** Dashboard (counts by stage, last 7 days, money waiting), parcels with tabs, search, date range and
  CSV export, parcel page (progress, route, money, attempts, history; edit and cancel before pickup, ask for a return
  while at a hub), labels (QR, A6, hub code), pickup requests, payments (statement and payouts) and invoices, fraud
  check, rate card and calculator, settings (profile, payout account bKash / Nagad / bank, pickup points), API keys,
  webhook.
- **Sign-up and approval.** `/Account/Register` makes a `Pending` merchant with its login and default pickup point and
  signs it in; the admin approves (`Active`), suspends or reactivates on `/Admin/Merchant/{id}`, or adds a merchant
  directly (`/Admin/NewMerchant`, active at once).
- **Hub.** "Choose your hub" first (`/Hub/Choose`: every hub with its waiting work, busiest first; the choice is
  remembered until the next sign-in, and the hub menu on each page shows each hub's waiting count). Board (live counts: pickups open, coming from other hubs, waiting for a rider, to send on, out with riders,
  to hand back, cash with riders), scan page with three modes (Receive, Dispatch, Hand back; the answer says what to do
  next), pickups (assign a rider of this hub), assign parcels to a rider (opens the rider's `DeliveryRun` for the day and
  a `DeliveryAttempt` per parcel), rider closing (cash received against expected; held and refused parcels go back on
  the shelf), parcel search and page (ask for a return), label reprints.
- **Rider app.** Today: cash in hand, stops with address, phone, COD; a delivery page per parcel with Delivered,
  Partly delivered (amount and reason), On hold (reason and date), Refused (reason); pickups to collect.
- **Admin.** Live dashboard (today's counts, stages, last 7 days, hubs, top merchants, cash with riders, owed to
  merchants), merchants, riders (add with a login, edit, stop), payouts (owed per merchant, run now, payouts and
  invoices), rates (change for new bookings only), coverage, failed messages (send again).
- **Public.** Landing page: the promise, a tracking box and the courier's real counts (cities, hubs, areas) beside an
  animated line drawing (a rider driving round a little planet of shops, a hub and homes, parcels on parachutes, cards
  following one parcel), a ribbon of services, the four steps drawn and animated, services, the rate card and a closing
  call to action; tracking page (status per step, the hub, a hold's reason; never the address, phone or amounts; rate
  limited per address); coverage list.
- **Texts and webhooks.** `Parcel` raises `ParcelBooked` once, at booking, and `ParcelStatusChanged` on every move after
  that; the save writes a `RecipientTextMessage` for the booking (a tracking link), for out for delivery, delivered and
  partly delivered, and a `ParcelStatusChangedMessage` (webhook) for every move — booking itself never posts to the
  webhook, since the merchant made that change itself. Texts are written at send time and skipped when out of date;
  webhooks follow Standard Webhooks (`parcel.status_changed`). Retries after 1, 2, 4, 8 minutes, then given up.
- **Fraud check.** Counts a phone's parcels at every merchant of the courier (delivered, partly, returned, in
  progress) with the merchant filter lifted, and returns counts and a verdict only.
- **Live dashboards.** After a committed save touching parcels, runs, attempts, pickups or riders, the courier's SignalR
  group hears "changed" and the page re-reads its `data-live` parts.
- **Not yet:** real SMS and bKash/Nagad/bank gateways (fakes), an admin screen to add hubs/zones/areas (seed only),
  printing a run sheet, the app's own SQL login instead of `sa`, merchant notifications of payouts, COD reconciliation
  reports over time.

### Database
- Schemas: `Platform` (Tenant), `Identity`, `Network` (Hub, Zone, Area), `Pricing` (DeliveryRate), `Merchants`
  (Merchant, MerchantApiKey, PickupPoint), `Parcels` (Parcel, ParcelEvent, sequence TrackingNumber → `OD10000001`),
  `Delivery` (Rider, DeliveryRun, DeliveryAttempt, PickupRequest), `Payments` (LedgerEntry, Payout, sequence
  PayoutNumber → `INV-000001`), `Notifications` (OutboxMessage).
- Every tenant table has `TenantId` + FK + index, the housekeeping columns, and its predicates in the security policy
  `Platform.TenantIsolation` (17 tables).
- Seed (`001_SeedCourier.sql`): OneDrop Courier (Asia/Dhaka, BDT, SMS sender `OneDrop`, hotline 09610-001122, 3
  attempts); 16 hubs (Dhaka city 5; suburbs Savar, Gazipur, Narayanganj; Chattogram, Sylhet, Rajshahi, Khulna,
  Barishal, Rangpur, Mymensingh, Cumilla), 21 zones, 87 areas; the rate card above. Demo logins, merchants, riders and (in Development)
  about twenty sample parcels come from `DemoDataSeeder` / `DemoActivity` at app start.

---

## 4. Decisions and the reasons for them

| Decision | Why |
|---|---|
| **2026-10-04: pivot to a traditional courier** (owner): no grouping, one parcel = one delivery; charges by service area and weight; COD charge; next-day payouts | The owner wants the product to work like Bangladesh's couriers (Steadfast) |
| Start the rebuild from the last commit (`3b48bc9`); the uncommitted Week 5 rewrite stashed (owner) | Clean base; nothing lost |
| Keep the multi-tenant base and all three isolation layers, seed one courier (owner) | The isolation work is sound and lets a partner courier be added later without a rewrite |
| Hub pages ask which hub first; the admin's dashboard stays courier-wide (owner's review, 2026-10-04) | Staff work at one hub, but an admin locked to one hub would miss work waiting at the others |
| Reset both databases and replace the DbUp history with one new seed (owner) | The old schema and data were the grouping product's; a fresh start is simpler than migrating |
| Recipients have no accounts: public tracking by code and SMS only; the phone sign-in is gone (owner) | That is how couriers work; the tracking code is the key |
| Service area from the pickup and destination zones (`Zone.City`, `Zone.IsSuburb`) | Couriers price inside city, suburb and outside city; the zone already belongs to a city |
| Charges snapshot on the parcel at booking | A rate change must never reprice a parcel already booked or invoiced |
| COD charge rounded to whole taka, away from zero | Money shown and paid in whole taka; ৳12.50 → ৳13 is what couriers do |
| Merchants sign up and wait for approval; admins can add active merchants directly | Couriers vet merchants before collecting cash for them |
| Pickups are requests the hub assigns to a rider; a merchant may also drop parcels at a hub (`Pending` → `AtHub`) | Both happen in real life |
| A rider's day is a `DeliveryRun` per rider per day; each hand-over is a `DeliveryAttempt` | The run carries the cash check; attempts count towards the courier's maximum |
| On hold only while attempts remain; the last failed attempt returns the parcel | Couriers try three times, then return |
| Returns travel back to the pickup hub and are handed back there; the merchant pays delivery + return charge | The pickup hub is where the merchant's parcels come from |
| Payouts hourly, every line up to yesterday, idempotency key per payout; lines waiting when charges exceed cash | Next-day payout must not slip; a charge is carried, never lost |
| Public tracking shows each step by its status (a hold keeps its reason), never notes, address, phone or amounts | Notes can hold amounts or reasons meant for the merchant; a code is easy to pass around |
| Fraud check across merchants returns counts only | Merchants value shared refusal data; no merchant learns another's customers |
| Schema owned by a SQL project + DbUp, no EF migrations | The owner's DCN setup; `SchemaMatchesModelTests` catches drift |
| Hangfire owns its `HangFire` schema | Third-party tables upgraded by Hangfire itself |
| Jobs take the tenant as a parameter, stored by name | A slow courier never holds up another; Hangfire cannot load generic methods |
| No product-name prefix in code | Owner's preference (DCN style) |
| No hard-coded business values: prices, attempts, time zone, sender name are data | Owner's rule; each courier states its own |
| Razor Pages (no SPA); custom CSS design system with tokens, a dark sidebar shell, inline SVG icons | One deployment, host-only cookies per courier, a clean "courier software" look without a front-end build |
| Tenant from the subdomain (panels) or API key (API) | Host-only cookies keep couriers apart |
| API keys `od_{12-char prefix}_{32-char secret}`, only a SHA-256 hash stored | The prefix finds the key before the tenant is known |
| Outbox in the change's transaction; texts written at send time; webhooks in their own loop | A change never saves without its message; a slow merchant server never delays an SMS |
| Integration tests use `OneDrop-Test`; each test makes its own merchants and riders | Test data never touches dev; parallel tests never count each other's parcels |
| Secrets only in git-ignored `*.Local.json` | The repository never holds the password |
| 2026-10-05: a rider with parcels, an open run or an assigned pickup cannot be stopped or moved to another hub | A stopped rider's app shows no work and a moved one hands cash in at the wrong hub, so that work would be stuck |
| 2026-10-05: a pickup point cannot move to another zone while parcels or a pickup wait there | Those parcels were priced, and the pickup sent to a hub, by the old zone |
| 2026-10-05: a hold's "deliver on" day must be after today | A past day would put the parcel straight back in the queue as if the customer had asked for it |
| 2026-10-05 UI: success is a toast that goes by itself, a problem stays on the page; anything hard to undo asks first (`data-confirm`, a styled dialog); motion only on first paint (`html.js-enter`) and never for reduced-motion users | People read problems, not confirmations; live dashboards must not jump on every update |
| 2026-10-05 brand (owner): a new logo, a drop-shaped map pin holding a taped parcel and landing on a ripple, on a sky blue tile; colour kept to ink on cool light grey paper with one accent, sky blue `#7cc6f2` (`--accent`; `--accent-text` `#0b6a9e` for text), flat fills, no gradients or glows; display headings in Bricolage Grotesque | The green made the product look like a copy of Steadfast; ultraviolet was rejected as looking AI-generated and marigold as too yellow; the owner asked for something light like sky blue. One exact door for each parcel is the name's promise |
| 2026-10-05 front page: drawn, looping animations (line art, ink and sky blue) only on the public front page; they rest while off screen and stay still for reduced-motion users | The owner asked for a cartoon animation like Steadfast's, but not a copy: a little planet instead of their road strip |

---

## 5. Environment

| Item | Value |
|---|---|
| Machine | Windows 11, .NET SDK 10.0.4xx, PowerShell 5.1 + Git Bash, Node 25 (for screenshot scripts) |
| SQL Server | `10.50.0.1,1433` over the VPN (the host `ras-x2` does not resolve), SQL Server 2025, login `sa`. Shared with the team |
| Databases | `OneDrop` (development), `OneDrop-Test` (integration tests). **Ask before creating any other** |
| Connection strings | `src/Web/appsettings.Local.json` and `tests/Integration.Tests/testsettings.Local.json`, git-ignored |
| Tools | `sqlpackage` (dotnet global tool), `sqlcmd`, Docker Desktop, GitHub CLI at `C:\Program Files\GitHub CLI\gh.exe` |
| Repository | https://github.com/talha33177net-eng/OneDrop (private), local folder `C:\Courier Project`; the rebuild is on branch `traditional-courier` |
| Git identity | Talha Ahmed &lt;talha33177.net@gmail.com&gt; |
| App URLs | http://onedrop.localhost:5080 (the courier), http://localhost:5080 (platform) |
| Demo logins, API keys | [README](../README.md) — password `OneDrop#2026` (Development only) |

### Everyday commands
```powershell
./tools/db/publish.ps1                           # update the dev database (after any schema or DbUp change)
./tools/db/publish.ps1 -Database OneDrop-Test    # update the test database (same trigger)
dotnet build Courier.sln
dotnet test --project tests/Domain.Tests
dotnet test --project tests/Architecture.Tests
dotnet test --project tests/Integration.Tests    # must report succeeded, not skipped
dotnet run --project src/Web
```

---

## 6. Working rules agreed with the owner

1. **Test every piece of work right after it is implemented** — build, new tests, republish databases if the schema
   changed, all three suites green, a live check in the running app; then update the plan's daily log.
2. **Follow DCN** (`C:\Git\DCN`) for conventions unless this project says otherwise.
3. **No product-name prefix** in projects, folders, namespaces or class names.
4. **Commits are authored by the repository owner only.** No co-author trailers, no "generated with" lines, no
   tool-named files in the repository.
5. **Never commit secrets.** Connection strings stay in `*.Local.json`. Scan staged files before every push.
6. **Ask before creating databases** on the shared server, and before anything that is hard to undo.
7. Leave changes uncommitted for review unless asked to commit or push.
8. Keep this file and the plan up to date as part of the work.
9. **Never hard-code business values**: they are courier data. Only the seed script and tests hold concrete numbers.

---

## 7. Traps already found (do not rediscover them)

| Trap | What to do |
|---|---|
| DbUp journals scripts by embedded resource name, which includes the project's root namespace | Never rename the namespace or folders of `Database Update` without updating `dbo.SchemaVersions` |
| `PRINT` without `;` before a `WITH` CTE in the post-deploy script fails the publish | End statements before a CTE with `;` |
| PowerShell 5.1 turns native stderr into terminating errors under `$ErrorActionPreference = 'Stop'` | Scripts use `Continue` and check `$LASTEXITCODE` |
| Piping `publish.ps1` into `Select-Object -First/-Last` stops it early (exit 255) | Log to a file and read the file |
| `WebApplicationFactory` starts its host lazily; parallel tests raced the seeders | The fixture starts the host once in `InitializeAsync` |
| The web app reads `appsettings.Local.json`; tests boot the web app | Tests override the connection in `ConfigureAppConfiguration`; tests only write to `OneDrop-Test` |
| xUnit v3 on .NET 10 needs Microsoft.Testing.Platform | `global.json` opts in; `dotnet test --project <path>`; filter with `-- --filter-class <name>` |
| An EF store-generated string with a non-null default skips the sequence | Tracking and payout numbers start as `null!` |
| `DATETIME2(0)` rounds | The interceptor truncates `Created` to whole seconds |
| The machine-wide NuGet config includes DCN's private feed | Repo `nuget.config` uses nuget.org only |
| curl keeps no cookies for `*.localhost` | Use `--resolve onedrop.localhost:5080:127.0.0.1` and pass cookies in a `Cookie` header |
| Microsoft.Build.Sql 2.2.0 breaks builds inside Visual Studio | Stay on 2.1.0 |
| SqlPackage blocks NULL → NOT NULL on a table with rows | Backfill and alter in a guarded `Scripts/Pre` script |
| `sqlcmd` runs with `QUOTED_IDENTIFIER OFF` | Pass `-I` when running scripts by hand |
| A running `dotnet run --project src/Web` locks `src/Web/bin` | Stop the app (`taskkill //F //IM Web.exe`) before rebuilding or testing |
| A test that tries to write another tenant's rows really writes them when isolation is broken | Isolation tests write each row's own value back |
| Since Row-Level Security, SqlPackage refuses table rebuilds | `publish.ps1` passes `AllowUnsafeRowLevelSecurityDataMovement` (safe: its connection is unrestricted) |
| Hangfire cannot load a generic job method back | Job methods are never generic (jobs go by name) |
| MARS is on, so EF cannot use savepoints in the outbox transaction | `SavepointsDisabledBecauseOfMARS` is ignored |
| Save entities that raise events with `SaveChangesAsync` | The sync `SaveChanges` refuses them |
| EF cannot filter after a constructor projection, nor translate `group by` then `join` | Project with member initialisers; aggregate first, then look up names in a second query |
| `~/` links are fingerprinted by `MapStaticAssets` | Use a plain path for the web manifest and service worker |
| Razor HTML-encodes `৳` written inside a C# string | Use `Money.Taka(x)` (an `HtmlString`) or keep the symbol in markup |
| Plus Jakarta Sans (the UI font) has no ৳ glyph; Windows falls back to an odd one | `_Fonts.cshtml` loads Noto Sans Bengali for that one character (`text=%E0%A7%B3`) |
| `.mono` sets `font-size: .92em`, which shrinks an `h1` | `h1.mono` sets its own size |
| A grid container counts every child: a stray `<label>` or `<input>` takes a cell | Hidden helpers (`#nav-open`, `.nav-scrim`) are `display: none` outside the phone layout |
| Razor's encoder turns `+` and `=` into entities | Tests `WebUtility.HtmlDecode` pages before searching them |
| Public pages are rate limited per address, and all test requests share one | Each test `Visitor` sends `X-Test-Client` with an address of its own |
| Headless Chrome's `--window-size` is never narrower than its minimum | Use DevTools emulation (`Emulation.setDeviceMetricsOverride`, width 390, `mobile: true`) and compare `scrollWidth` with `clientWidth` |
| The VPN link to the SQL Server can be slow | Check `sys.dm_exec_sessions`; stop and rerun a stalled publish |
| A CSS rule beats an SVG presentation attribute (`stroke-width="5"` loses to `.o { stroke-width: 2.4 }`) | In the drawings, a one-off stroke or fill goes in `style="…"` |
| Headless screenshots do not move animations that run on the compositor (the planet's turn) | To see a frame, pause every animation at a time: `document.getAnimations().forEach(a => { a.pause(); a.currentTime = t; })` |
| A short class name on a page can collide with a shared partial's (`.step` vs `.progress-steps .step`) | Front-page classes are prefixed (`how-step`, `hero-…`); search `Pages` before adding a bare one |

---

## 8. Glossary

| Term | Meaning |
|---|---|
| Tenant / courier | An operator (OneDrop Courier). Selected by subdomain or API key |
| Merchant | A shop that sends parcels. Sees only its own |
| Service area | Inside city, suburb or outside city: decides the price |
| Hub | A sorting point; each zone is served by one hub |
| Pickup hub / delivery hub | The hub of the pickup point's zone / of the recipient's area |
| Run | One rider's deliveries on one day, closed with the cash handed in |
| Attempt | One hand-over of a parcel to a rider and its outcome |
| COD | Cash on delivery, collected at the door and paid to the merchant the next day less charges |
| Payout | One payment to a merchant, with an `INV-` invoice of its ledger lines |
