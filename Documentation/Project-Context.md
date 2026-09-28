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
| 2 | Grouping core | 3 shops' orders form 1 group | ✅ Done 2026-09-28 |
| 3 | Operations and money | Group delivered, merchants settled | 🔄 3.1–3.4 done, next 3.5 |
| 4 | Polish and proof | Full demo runs end to end | ⬜ |

Task-level detail, the cut list, the job schedule, must-pass tests and the daily log are in
[Plans/Implementation-Plan.md](../Plans/Implementation-Plan.md). **That file is the source of truth for progress.**

---

## 3. What exists today (Weeks 1 and 2, tasks 3.1–3.4)

### Solution layout (`Courier.sln`)
| Project | Path | Contents |
|---|---|---|
| Domain | `src/Domain` | Entities and rules, no packages. `Common` (Entity with domain events, TenantEntity, Result/Error), `Platform/Tenant`, `Network` (Hub, Zone, Area, PickupRoute), `Customers` (Customer, CustomerAddress, PhoneNumber, PhoneOtp), `Merchants` (Merchant, MerchantApiKey, PickupPoint), `Orders` (Order + state machine and scans, Package with its hub, PackageLabel, OrderStatusHistory), `Grouping` (DeliveryGroup + state machine, lock time and shelf, events), `Delivery` (Rider, Trip, TripStop, TripLoad, TripPlanner), `Pricing` (DeliveryFeeCalculator, FeeSchedule), `Notifications` (OutboxMessage + retry rule) |
| Application | `src/Application` | Use cases as vertical slices: `Orders/CreateOrder`, `Orders/GetOrder`, `Network/ListAreas`, `Network/PickupRoutes` (route list and sheet), `Network/HubScan` (collect, receive at a hub, shelves, shuttle load and manifest), `Orders/PackageLabels`, `Auth/PhoneLogin`, `Customers/CustomerDirectory` (find-or-create by phone/address), `Grouping/DeliveryGrouping` (join or open the order's group; quote), `Grouping/LockDueGroups` (the lock job), `Grouping/ShipNow`, `Grouping/CustomerDeliveries` ("My deliveries"), `Delivery/PlanTrips` (planner and job), `Delivery/HubTrips`, `Delivery/RiderDay` (the rider's stops, Start trip), `Pricing/GetQuote`, `Notifications` (outbox contracts, `SendOutbox` job and SMS texts). Interfaces: `IAppDbContext`, `ITenantJob`, `ITenantContext` (`TenantInfo.Fees`), `ITenantCatalog`, `ICurrentUser`, `ISmsSender`; `QueryFilters` (filter names) |
| Infrastructure | `src/Infrastructure` | `Persistence/AppDbContext` (EF Core, query filters), `Configurations/*` (mapping), `TenantSaveInterceptor`, `MultiTenancy` (TenantContext, TenantCatalog), `Identity` (AppUser, AppRole, claims), `Sms/FakeSmsSender`, `Seeding/DemoDataSeeder`, `Jobs` (Hangfire setup, TenantJobRunner, TenantJobRegistry, OutboxDispatcher); `AppDbContext.SaveChangesAsync` writes the outbox |
| Web | `src/Web` | Razor Pages portals (merchant, customer, hub, platform), `Labels/LabelQrCode` (QRCoder), `Api/V1` (orders, quote, areas by API key; deliveries by customer cookie), `Authentication/ApiKeyAuthenticationHandler`, `MultiTenancy` middleware, `Program.cs` |
| Database | `src/Database` | SQL project (Microsoft.Build.Sql 2.1.0) → `Database.dacpac`. Owns the schema |
| Database Update | `src/Database Update` | DbUp console (`dbup.exe`): data migrations in `Scripts/<Year>/`, data-loss scripts in `Scripts/Pre/` |
| Tests | `tests/Domain.Tests`, `tests/Architecture.Tests`, `tests/Integration.Tests` | 170 + 6 + 86 = **262 tests, all passing** |
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
- **Pricing** (`Domain/Pricing/DeliveryFeeCalculator`, the tenant's prices): group fee = base + extra × (distinct
  shops − 1), counting only orders not cancelled, refused or returned; Deliver fast = the fast fee; Don't hold
  alone = the base fee. Each order stores `AddedFee` (base, extra, or 0 for a shop already in the group), and
  Create Order / Get Order return it as `fee`. The group total is never shown to a merchant.
- **Checkout quote** `GET /api/v1/quote?phone=&area=&line1=` (optional `areaId`, `line2`, `speed`, `doNotHold`):
  `{ fee, currency, joinsDelivery }`, the fee Create Order would give the same order now (৳60, +৳25, or 0 for a
  shop already in the delivery). Read only: creates no customer or address. `DeliveryGrouping.QuoteAsync` picks
  the group the same way Create Order does and shares its fee query.
- **Background jobs** (Hangfire, `Infrastructure/Jobs`): the recurring `lock-due-groups` (every 5 minutes,
  `Jobs:LockDueGroups`) queues `LockDueGroupsJob` once per active tenant; the runner sets the tenant from the
  job's parameter. Open groups past their deadline become `Locked` with `LockedOn` = the deadline. A new tenant job
  implements `ITenantJob`, is added to `TenantJobRegistry` and gets a recurring entry in `JobsSetup.ScheduleJobs`.
  Dashboard: http://localhost:5080/jobs (platform admin). `Jobs:Server` = false runs no server (integration tests).
- **Ship now** (`Application/Grouping/ShipNow`, `DeliveryGroup.ShipNow`): the signed-in customer closes an open
  delivery; it locks at once and is delivered the **next day** (`LocksAt` = the next tenant midnight). From the
  "My deliveries" page (each open delivery with its shops, last day to join, delivery day and a button) or
  `POST /api/v1/deliveries/{number}/ship-now` with the customer's sign-in cookie → `{ number, deliveryDate }`;
  a closed delivery is 409, anyone else's is 404. The sign-in cookie answers `/api` with 401/403, not a redirect.
- **Outbox and SMS** (`Application/Notifications`): entities raise domain events (`Order.PlaceIn` →
  `OrderPlacedInDelivery`; `LockIfDue` / `ShipNow` → `DeliveryGroupLocked`). `AppDbContext.SaveChangesAsync`
  writes them to `Notifications.OutboxMessage` in the same transaction (save, add rows with the new ids, save,
  commit). `SendOutboxJob` texts the customer (joined an open delivery and until when it can grow; travelling alone
  and the day; delivery closed, its shops and the day), written from current data at send time; a failure retries
  after 1, 2, 4, 8 minutes, then `Failed`. The in-process `OutboxDispatcher` runs it for every tenant every
  `Jobs:OutboxInterval` (5 s) when `Jobs:Server` is on. A new message: a contract record and a case in
  `OutboxContracts.ToOutbox`, and its text in `CustomerTexts`.
- **Customer app** (`Application/Grouping/CustomerDeliveries`, `Pages/Customer/Index`): "My deliveries" shows one
  card per delivery on its way (and the 10 latest finished): status, delivery day, last day to join, each shop's
  order with packages and status, packages collected, fee so far (recalculated on what is still for delivery),
  COD, the total at the door and "You save ৳70 against 3 separate deliveries" (tenant base fee × shops − group
  fee), with Ship now on open ones. It installs on a phone: `wwwroot/manifest.webmanifest`, icons, `sw.js`
  (no page caching, `offline.html` without a signal), linked by `Shared/_CustomerApp` in the layout's `Head` section.
- **Pickup routes and labels** (3.1): `Network.PickupRoute`, one active route per zone leaving at a tenant-set
  local time (2 PM for the launch zones). Hub staff and tenant admins see **Pickup routes** (`/Hub/Routes`: next
  run, stops, orders, packages per zone) and a printable sheet per route (`/Hub/RouteSheet/{id}`): each pickup
  point in the zone with orders still `Created`, their labels, "Next day" on Deliver fast and Don't hold orders.
  Worked out when opened, no job. Merchants print QR labels (`/Merchant/Labels`, linked from "My orders"): one per
  parcel, `OD-100001-1` (`PackageLabel`), with the destination hub code, recipient, area, shop and COD, never the
  delivery group.
- **Scanning and shelves** (3.2, `Application/Network/HubScan`): hub staff pick their hub (`/Hub/Scan?hub=MIR`)
  and scan labels (hand scanner into the box, or the phone camera where the browser has `BarcodeDetector`).
  **Collect at the shop** moves the whole order `Created` → `PickedUp` (it leaves the route sheet). **Receive at the
  hub** records each package's hub and time (`Package.HubId`, `ReceivedOn`); the order is `AtHub` once every
  package is in. At the hub its delivery leaves from, the delivery takes the lowest free shelf there
  (`DeliveryGroup.Shelf`, shown as `MIR-01`) and keeps it until it is dispatched, delivered or cancelled; at another
  hub the answer is "Send on to GUL" and no shelf. A repeated scan says so and changes nothing; another operator's
  label is "not found". `/Hub/Shelves?hub=MIR` lists each shelf's delivery, day, orders and parcels here of total,
  Ready once locked and complete. Two scans at once are settled by the row version and `UX_DeliveryGroup_Hub_Shelf`
  and retried from fresh rows.
- **Hub shuttle** (3.3): a parcel scanned in at a hub other than its delivery's is loaded with the scan tab **Load the
  shuttle** (`Order.LoadForShuttle`: off the hub, `Package.ShuttleToHubId` = the delivery's hub) and received at the
  other end with the normal scan, which shelves it. `/Hub/Shuttle?hub=MIR` is the printable manifest: parcels to
  load per destination hub, next-day ones first, and parcels on their way in. No job or time setting: staff load
  when the shuttle leaves. The order stays `AtHub` in transit (all packages have a `ReceivedOn`).
- **Riders and trips** (3.4, `Delivery` schema): a `Rider` works from one hub with a bike limit of parcels and
  weight (`MaxParcels`, `MaxWeightGrams`, per rider, no default) and signs in as staff (`Rider.UserId`, role
  `Rider`). `TripPlanning` (job `plan-trips`, every 15 minutes, `Jobs:PlanTrips`; also **Plan trips now** on
  `/Hub/Trips?hub=MIR`) puts each hub's deliveries due today (`Locked`, `LocksAt` passed, at least one parcel on the
  shelf) on its riders' trips: late ones first, then area by area, each to the first rider by name with room
  (`Domain/Delivery/TripPlanner`); one trip per rider a day (`Trip`, `Planned` → `Out`), a delivery on one trip a day
  (`TripStop`); a trip still planned after its day is cancelled. The rider's **Today** (`/Rider`, installable,
  `rider.webmanifest`): stops by area with recipient, address, landmark, phone, shelf, labels, readiness and what to
  collect (fee on what is taken + COD). **Start trip** hands over every order with all parcels on the shelf
  (`Order.HandToRider` → `OutForDelivery`, packages off the hub), dispatches its delivery and frees the shelf; a
  delivery with nothing ready comes off the trip and stays `Locked`.
- **Not yet:** the delivery screen and attempts (3.5), a screen to add riders or change a bike's limit, a screen to change route times, Ship now by SMS "reply 1" (needs an inbound SMS gateway), a screen for failed outbox messages,
  merchant screens to create API keys or enter orders manually, tenant admin screens.

### Database
- Schemas: `Platform` (Tenant), `Identity` (User, Role, UserRole, UserClaim, UserLogin, UserToken, RoleClaim),
  `Network` (Hub, Zone, Area, PickupRoute), `Customers` (Customer, CustomerAddress, PhoneOtp), `Merchants` (Merchant,
  MerchantApiKey, PickupPoint), `Orders` (Order, Package, OrderStatusHistory, sequence OrderNumber → `OD-100001`),
  `Grouping` (DeliveryGroup, sequence DeliveryGroupNumber → `DG-100001`; one `Open` group per customer + address
  by filtered unique index), `Delivery` (Rider, Trip — one per rider a day, TripStop — a delivery on one trip a
  day), `Notifications` (OutboxMessage). `Order.DeliveryGroupId` is NOT NULL: every order travels in a group
  (`Scripts/Pre/001_GroupExistingOrders` grouped the orders saved before 2.2). `Order.AddedFee` is NOT NULL
  (`Scripts/Pre/002_PriceExistingOrders` priced the orders saved before 2.3). `Package.HubId`/`ReceivedOn` say
  where a parcel was last scanned in (null on the shuttle, when `ShuttleToHubId` says where it is going);
  `DeliveryGroup.Shelf` is unique per hub while set (`UX_DeliveryGroup_Hub_Shelf`).
- `Platform.Tenant` settings (fees, `GroupJoinDays`, time zone, currency, SMS sender) have **no defaults**, in
  SQL or C#: every tenant states its own. No business value is hard-coded anywhere.
- Every tenant table: `TenantId` + FK + index; housekeeping columns `Archived`, `UpdatedId`, `UpdatedOn`, `Created`.
- Seeded by DbUp `2026/001_SeedLaunchTenants.sql`: **OneDrop Dhaka** (id 1, slug `dhaka`, 7 zones on 5 hubs,
  32 areas, ৳60 + ৳25) and **OneDrop Chattogram** (id 2, slug `chattogram`, 5 zones on 2 hubs, 14 areas,
  ৳70 + ৳30). `2026/002_SeedPickupRoutes.sql` gives every launch zone a 2 PM pickup route. Roles are seeded by
  `Script.PostDeployment.sql`.
- Dev data in `OneDrop` right now: merchants 1–3 (Dhaka: Fashion House, Gadget BD, Beauty Shop) and 4–6
  (Chattogram, same names); orders OD-100001, OD-100002, OD-100004 (the three Dhaka shops, **same customer 1 and
  address 1, one group DG-100003**) and OD-100003 (Chattogram, same phone, different customer 2, DG-100004).
  OD-100005 onwards are the 2.2 live check (phone 01563583024: one Dhaka group of 10 orders, one fast order,
  one Chattogram order). OD-100017 to OD-100023 are the 2.3 live check (phone 01966225784: DG-100008 holds three
  shops for ৳110). OD-100024 to OD-100026 are the 2.4 live check (phone 01950973272, three shops, quoted and
  charged 60 / 25 / 25); for the 2.5 live check their group DG-100012 was given a deadline in the past by hand
  and locked by the job, and OD-100027 then opened DG-100013. OD-100028 to OD-100030 are the 2.6 live check
  (phone 01736878920, signed in as a customer): DG-100014 and DG-100015 were closed by Ship now for Tuesday
  29 September; 01991998650 is a second signed-in Dhaka customer. OD-100031 to OD-100034 are the 2.7 live check
  (phone 01764090796; outbox rows 1–5, all sent; DG-100016 closed by Ship now). Orders before OD-100031 have no
  outbox rows. OD-100035 to OD-100037 are the 2.8 live check (phone 01845127390, three shops with COD, DG-100019
  closed by Ship now from the page). OD-100038 to OD-100040 are the Week 2 demo run (phone 01912734580, the
  three Dhaka shops in DG-100020 for ৳110, locked by the job after its deadline was moved into the past by hand).
  OD-100041 (Fashion House, 2 packages) and OD-100042 (Gadget BD, fast) are the 3.1 live check (phone 01957461664).
  OD-100043 to OD-100046 are the 3.2 live check (phone 01834561290): OD-100043–45 collected/received at MIR, their
  DG-100023 on shelf MIR-01; OD-100046 (Gulshan 1) scanned at MIR then GUL, its DG-100024 on GUL-01.
  OD-100047 and OD-100048 are the 3.3 live check (phone 01745219083, Banani): OD-100048 (fast) went MIR → shuttle →
  GUL, shelf GUL-02; OD-100047 is still on the shuttle to GUL.
  Demo riders (dev only): Rafiq Hasan (`rider@dhaka`, MIR, 30 parcels / 25 kg), Sumon Ali (`rider2@dhaka`, MIR,
  12 / 15 kg), Kamal Uddin (`rider3@dhaka`, GUL), Jamal Chowdhury (`rider@chattogram`, AGR). OD-100049 to OD-100052
  are the 3.4 live check: DG-100027 (Farhana Akter, 01893456120; Fashion House OD-100049 out with Rafiq, Gadget BD
  OD-100050 never collected) and DG-100028 (OD-100051, fast) are out on Rafiq's trip of 28 September; DG-100029
  (OD-100052, 1 of 2 parcels on MIR-02) is planned on Sumon's trip. Their delivery days were moved to 28 September
  by hand, as were DG-100012 and DG-100020's earlier, which therefore show as due with no parcel at the hub.
  DG-100003 also totals ৳110. The Hangfire tables
  are installed at app start in both databases (mapping the dashboard opens the storage); only `OneDrop` runs jobs,
  as the integration tests start no job server. Group numbers have gaps: a sequence value used in a rolled-back
  dry run is not reused.

---

## 4. Decisions and the reasons for them

| Decision | Why |
|---|---|
| Modular monolith, Clean Architecture, 4 projects | From the plan: fast to build, modules can be split out later |
| **Schema owned by a SQL project + DbUp, no EF migrations** | The user asked for the database "just like DCN" (their project at `C:\Git\DCN`) so it is easy to maintain. `SchemaMatchesModelTests` fails if EF and SQL drift |
| Exception: Hangfire owns its `HangFire` schema (installs and upgrades it itself) | Third-party tables that change with the Hangfire version; copying them into the SQL project would mean hand-maintaining its upgrades. The publish profile never drops objects outside the project |
| Background jobs take the tenant as a parameter: one recurring entry per job queues one run per tenant, stored as (job name, tenant id) | A failing or slow tenant does not hold up another; a new tenant is picked up without new schedules; Hangfire cannot load generic methods |
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
| Create Order returns only this order's `fee` (base, +extra or 0, stored as `Order.AddedFee`), never a group total | Owner's choice (2026-09-28): the total would show how many other shops are in the delivery |
| The quote is read only, answers with the same fee Create Order would give, and says only `joinsDelivery` about the group | A checkout can call it for every visitor without creating customers; the "+৳25" is the product's promise and tells no more than the fee |
| Don't hold costs the base fee; Deliver fast costs the tenant's `FastDeliveryFee` | Owner's choice: Don't hold is the merchant's decision, not the customer's |
| A shop is charged only while it has an order not cancelled, refused or returned | "Fee is calculated on what is actually delivered" |
| **Frontend is Razor Pages** (PWA for customer and rider screens, SignalR for live dashboards); no React or Angular in the MVP | Owner's choice (2026-09-28). Keeps the host-only tenant cookies and one deployment; the screens are mostly forms and lists. React may be reconsidered for the rider app only (tasks 3.4–3.5) |
| Ship now delivers the next day (`LocksAt` moves to the next tenant midnight, never later) | Keeping Day 3 would only stop other shops joining; next day matches Deliver fast, and the lock job and trip planning keep reading `LocksAt` |
| The customer API (`/api/v1/deliveries`) uses the customer's sign-in cookie on the tenant subdomain, never an API key | A merchant key must never reach a delivery; the host-only cookie gives the tenant as for the pages |
| **Customers need no sign-up**: created automatically from the phone number on the first order; the SMS-code login is optional and opens the same record | Owner's choice (2026-09-28). Customers buy at the shop and may never visit OneDrop; grouping must work from their first order |
| Domain events go to the outbox in the same transaction as the change; rows hold ids only and the SMS is written when sent | A change never saves without its message, nor a message without its change; a retried text is never stale |
| The outbox sender runs every 5 seconds in process (`OutboxDispatcher`), not as a Hangfire recurring job | Hangfire recurring jobs run at most once a minute; "your order joined" should arrive during checkout |
| "You save" = the tenant's base fee × distinct shops − the group fee; nothing for one shop | The documentation compares with ৳60 per separate courier, the one-shop price; no second price setting to keep in step |
| `Order.IsForDelivery` (not cancelled, refused or returned) drives the fee, the COD due and the package count | One rule, so the customer page, the door and the fee never disagree |
| The customer app's service worker caches no pages, only the offline page; the manifest is linked by a plain path | Delivery data changes all day; `~/` would fingerprint the manifest URL per build |
| `Network.PickupRoute` (not `Route`), one active per zone, time set by the tenant; the sheet is worked out when opened | `Route` clashes with ASP.NET's `[Route]`; an order placed just before the run is on the sheet, and no job has to keep a stored list in step |
| A parcel is collected by the route of its **pickup point's** zone, not `Merchant.ZoneId` | That is where the parcel is; a merchant can have pickup points in several zones |
| Label = `{order number}-{package}` (`PackageLabel`), QR holds only that code; it shows the destination hub, never the group | The scanner's tenant decides whose parcel it is; a label must not tell a merchant about the customer's other shops |
| Hub pages use policy `Operations` (hub staff or tenant admin) | The operator's own staff; another operator's route is a 404 |
| Hub scans are per package (`Package.HubId`); `AtHub` = every package in. Collecting at the shop is per order | A delivery's parcels must all be on its shelf before the rider takes it; the collector takes the whole order |
| A delivery takes the lowest free shelf at its own hub at its first parcel and frees it when it leaves | Fixed, numbered shelves; the unique index settles two scans taking the same one |
| Hub staff choose their hub on the page (`?hub=MIR`), not from their login | One hub-staff login per operator in the MVP; a hub on the user can come with the rider app |
| The hub shuttle is a load scan and a receive scan; on the shuttle a package has no `HubId`, only `ShuttleToHubId` | "Scan at every handover"; a package is never at two hubs, and the manifest shows what is left to load |
| A bike's limit is `Rider.MaxParcels` + `MaxWeightGrams`, per rider, no default | Bikes differ; both numbers are already on every package |
| Trips are planned every 15 minutes from the start of delivery day (repeatable), not once at 08:00 | Parcels still arrive on delivery day; tenants have different time zones |
| A delivery is planned once one parcel is on its shelf; Start trip takes only orders with every parcel here, and a delivery with nothing ready comes off the trip | "Merchant not ready → the group leaves without it" |
| One trip per rider a day; riders filled in name order, late deliveries first, then area by area | One evening run; area order keeps stops together without geocoding |
| `TripStop.DeliveryDate` copies the trip's date for `UX_TripStop_DeliveryGroup_DeliveryDate` | The database settles the job and the button planning at once |
| `Rider.UserId` points at the login (riders sign in as staff on their subdomain) | Identity unchanged; a rider can exist before a login |

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
| Hangfire stores a generic method call but cannot load it back (`does not contain a method with signature …`); a direct call in a test works | Job methods are never generic (jobs go by name through `TenantJobRegistry`); `The_scheduled_job_and_the_jobs_it_queues_survive_hangfires_storage_format` round-trips them |
| To see a job run without waiting for its schedule | `dotnet run --project src/Web -- --Jobs:LockDueGroups="* * * * *"` (the next normal start resets the schedule) |
| MARS is on in the connection string, so EF cannot use savepoints inside the outbox transaction and warns on every save | The warning `SavepointsDisabledBecauseOfMARS` is ignored in `AddInfrastructure`: a failed save rolls the outbox transaction back whole |
| The fake SMS log keeps only the last 50 messages, and a test run drains every pending outbox row | Outbox tests record texts with their own `ISmsSender` and run the sender until their own message is handled |
| Save entities that raise events with `SaveChangesAsync` | The sync `SaveChanges` refuses them: only the async path writes the outbox |
| EF cannot filter or sort after a projection into a constructor (`select new Row(a, b)` then `.Where`) | Project with member initialisers (`new Row { A = a }`) when the query is composed further; a constructor is fine in the final `Select` |
| `~/` links are rewritten by `MapStaticAssets().WithStaticAssets()` to a fingerprinted URL | Use a plain path for anything that needs a stable address (the web manifest, the service worker) |
| Razor HTML-encodes `৳` (and other non-Latin text) written inside a C# expression (`@($"৳{x}")` → `&#x9F3;`) | Keep the symbol in the markup: `৳@amount.ToString("N0")` |
| The integration database holds hundreds of waiting Mirpur orders from earlier runs | Tests that count what is waiting use shops of their own in another zone (Uttara), and lists must not assume a short backlog |
| Test classes that count what waits on the Uttara pickup route (`PickupRouteTests`) race any class opening an Uttara shop (`HubScanTests`) | They share the xUnit collection `"Uttara pickups"`; a new class using Uttara pickup points joins it. Trip tests build a hub, zone and area of their own instead |
| `chrome --headless --window-size=390,…` lays pages out wider than 390 px (a minimum window width), so a screenshot looks cut off | Use DevTools mobile emulation (`Emulation.setDeviceMetricsOverride`, width 390, `mobile: true`) and compare `scrollWidth` with `clientWidth` |
| The link to ras-x2 is sometimes slow (DNS takes seconds): sqlpackage can stall before connecting, tests can hit a login timeout | Check `sys.dm_exec_sessions` for the process; if it has no session, stop it and run again. Do not pipe `publish.ps1` into `Select-Object -Last`, which hides its progress |

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
| Trip / stop | One rider's run from their hub on one delivery day / one delivery on it |
| COD | Cash on delivery: product money collected at the door, settled to the merchant next day |
| Trust score | Customer record of refusals and no-shows; low score = pay the fee first |
| Day 1 / 2 / 3 | First order day / last day to join / delivery day (tenant time zone) |
