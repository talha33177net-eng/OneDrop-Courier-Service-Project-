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
- **Deliver fast:** next-day delivery, no waiting, at the tenant's fast fee (the documentation said ৳60; Dhaka
  charges ৳70 since the market review below). **Don't hold:** merchants flag food, medicine or dated gifts to skip
  waiting.
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

### The Bangladesh market
Reviewed 2026-09-28 from the couriers' own pages and 2026 comparison articles (list prices; big merchants get
negotiated rates). What Dhaka couriers charge the **merchant** inside Dhaka city:

| Courier | Up to 1 kg | Heavier | COD fee in Dhaka | Failed delivery in Dhaka | Speed |
|---|---|---|---|---|---|
| Steadfast | ৳55 | by weight | 1% | a return cost is added | 1–2 days; same day ৳105 |
| Pathao | ৳60 (500 g), ৳70 (1 kg) | ৳90 at 2 kg, +৳15 per kg | 0.5% | normally free | 24 h from pickup; 4–6 h express |
| RedX | ৳60 | ৳75 at 2 kg, ৳90 at 3 kg | 0% | not published | next day |
| Paperfly | ৳70 (VAT included) | +৳20 per kg | 0% | one delivery charge | next day |

- Suburbs (Savar, Gazipur, Narayanganj) ৳80–110; outside Dhaka ৳110–135. Pathao pays merchants daily.
- The merchant pays the courier and usually charges the customer ৳60–80 inside Dhaka, inside the COD amount.
  Three shops sent separately therefore cost the customer ৳180–240; OneDrop charges ৳110.
- Courier times count from pickup: a Facebook order (ordered at night, confirmed next day, picked up) usually
  reaches the door on Day 3 anyway, so OneDrop's Day 3 at ৳60 is the market's price and speed.
- 20–30% of COD parcels come back; a failed COD delivery costs ৳100–200 in two-way fees. Asking for the delivery
  charge in advance by bKash is common with Facebook sellers and cuts fake orders. Websites that look up a phone's
  history across couriers exist, so merchants value shared refusal data.

What the review changed is in the plan's decisions log (2026-09-28, "Market review") and in tasks 3.4a–3.8, 4.1,
4.7 and 4.8.

Sources: [Steadfast pricing](https://www.steadfast.com.bd/pricing),
[Steadfast charges 2026](https://banikh.com/en/blog/steadfast-courier-charge),
[courier comparison 2026](https://banikh.com/en/blog/best-courier-for-online-business-bangladesh),
[Pathao help](https://help.pathao.com/what-is-the-delivery-charge-inside-or-outside-the-city/),
[Pathao rates 2026](https://bizmend.com/blog/pathao-courier-service-ecommerce-bangladesh/),
[RedX rates](https://couriertrace.com/blog/redx-courier-service-delivery-charge-cost-and-price-list/),
[Paperfly charges](https://paperfly.com.bd/charges/),
[COD return rates](https://easysellapp.com/blogs/wiki/cod-ecommerce-pakistan-bangladesh-cash-collection-delivery-2026).

### Source documents
The user supplied two PDFs (not stored in the repo): *OneDrop Implementation Plan* (4-week technical plan) and
*OneDrop Project Documentation* (product, pricing, rules). Their content is captured here and in the plan.

---

## 2. The 4-week MVP plan and where we are

| Week | Theme | Done when | Status |
|---|---|---|---|
| 1 | Foundation | An order can be created for a tenant | ✅ Done 2026-09-27 |
| 2 | Grouping core | 3 shops' orders form 1 group | ✅ Done 2026-09-28 |
| 3 | Operations and money | Group delivered, merchants settled | 🔄 3.1–3.6a done, next 3.6b (confirmation and advance payment) |
| 4 | Polish and proof | Full demo runs end to end | ⬜ |

Task-level detail, the cut list, the job schedule, must-pass tests and the daily log are in
[Plans/Implementation-Plan.md](../Plans/Implementation-Plan.md). **That file is the source of truth for progress.**

---

## 3. What exists today (Weeks 1 and 2, tasks 3.1–3.6a)

### Solution layout (`Courier.sln`)
| Project | Path | Contents |
|---|---|---|
| Domain | `src/Domain` | Entities and rules, no packages. `Common` (Entity with domain events, TenantEntity, Result/Error), `Platform/Tenant`, `Network` (Hub, Zone, Area, PickupRoute), `Customers` (Customer, CustomerAddress, PhoneNumber, PhoneOtp), `Merchants` (Merchant, MerchantApiKey, PickupPoint), `Orders` (Order + state machine and scans, Package with its hub, PackageLabel, OrderStatusHistory), `Grouping` (DeliveryGroup + state machine, lock time and shelf, events), `Delivery` (Rider, Trip, TripStop, TripLoad, TripPlanner), `Pricing` (DeliveryFeeCalculator, FeeSchedule), `Payments` (Payment: cash or wallet, pending until paid), `Notifications` (OutboxMessage + retry rule) |
| Application | `src/Application` | Use cases as vertical slices: `Orders/CreateOrder`, `Orders/GetOrder`, `Network/ListAreas`, `Network/PickupRoutes` (route list and sheet), `Network/HubScan` (collect, receive at a hub, shelves, shuttle load and manifest), `Orders/PackageLabels`, `Auth/PhoneLogin`, `Customers/CustomerDirectory` (find-or-create by phone/address), `Grouping/DeliveryGrouping` (join or open the order's group; quote), `Grouping/LockDueGroups` (the lock job), `Grouping/ShipNow`, `Grouping/CustomerDeliveries` ("My deliveries"), `Delivery/PlanTrips` (planner and job), `Delivery/HubTrips`, `Delivery/RiderDay` (the rider's stops, Start trip), `Delivery/Door` (at the door: amount due, QR, hand over, nobody home), `Pricing/GetQuote`, `Notifications` (outbox contracts, `SendOutbox` job and SMS texts). Interfaces: `IAppDbContext`, `ITenantJob`, `ITenantContext` (`TenantInfo.Fees`), `ITenantCatalog`, `ICurrentUser`, `ISmsSender`, `IPaymentGateway`; `QueryFilters` (filter names) |
| Infrastructure | `src/Infrastructure` | `Persistence/AppDbContext` (EF Core, query filters), `Configurations/*` (mapping), `TenantSaveInterceptor`, `MultiTenancy` (TenantContext, TenantCatalog), `Identity` (AppUser, AppRole, claims), `Sms/FakeSmsSender`, `Payments/FakePaymentGateway` (paid by hand on `/Dev/Payments`), `Seeding/DemoDataSeeder`, `Jobs` (Hangfire setup, TenantJobRunner, TenantJobRegistry, OutboxDispatcher); `AppDbContext.SaveChangesAsync` writes the outbox |
| Web | `src/Web` | Razor Pages portals (merchant, customer, hub, platform), `Labels/LabelQrCode` (QRCoder), `Api/V1` (orders, quote, areas by API key; deliveries by customer cookie), `Authentication/ApiKeyAuthenticationHandler`, `MultiTenancy` middleware, `Program.cs` |
| Database | `src/Database` | SQL project (Microsoft.Build.Sql 2.1.0) → `Database.dacpac`. Owns the schema |
| Database Update | `src/Database Update` | DbUp console (`dbup.exe`): data migrations in `Scripts/<Year>/`, data-loss scripts in `Scripts/Pre/` |
| Tests | `tests/Domain.Tests`, `tests/Architecture.Tests`, `tests/Integration.Tests` | 211 + 6 + 96 = **313 tests, all passing** |
| Tools | `tools/db/publish.ps1` | Deploys a database: `dbup pre` → dacpac publish → `dbup` |

### Features that work
- `POST /api/v1/orders` (API key in `X-Api-Key`, optional `Idempotency-Key`): validates per field, finds or
  creates the customer by phone and the address by match key, resolves area → zone → hub, saves order + packages
  + first status in one save. Same key + same body → 200 with the same order; same key + other body → 409.
- **Grouping on create** (`Application/Grouping/DeliveryGrouping`): in the same save the order joins the
  customer's delivery to that address that leaves soonest and can still take it (`DeliveryGroup.CanTake`): the open
  group (window = the tenant's `GroupJoinDays` in its time zone), or a locked next-day delivery (`Kind` `NextDay` or
  `ShippedNow`, not yet out) while the order's pickup route next runs on or before its delivery day. Otherwise a
  waiting order opens a group and a Deliver fast or Don't hold order a next-day delivery (locked at once, delivered
  tomorrow); a fast or Don't hold order joins the open group only when that arrives tomorrow. A locked `Waiting`
  group takes nothing, so an order on Day 3 starts a new group. An open group
  past its deadline is locked on the spot. Two orders opening the same group at once: the unique index refuses
  one, which then joins the winner's group. The response never shows the group (merchant privacy).
- `GET /api/v1/orders/{number}` (own orders only; others 404), `GET /api/v1/areas`.
- Staff login (email + password) per tenant subdomain; customer login by SMS code; the fake SMS sender shows codes
  at `/Dev/Sms` in Development.
- Pages: landing (platform or tenant), merchant order list + API key list, customer "My deliveries", platform
  tenant list (the only cross-tenant page).
- **Pricing** (`Domain/Pricing/DeliveryFeeCalculator`, the tenant's prices): group fee = first shop + extra ×
  (distinct shops − 1) + each started kg a shop's orders weigh above `WeightAllowanceGrams` × `ExtraKgFee`, counting
  only orders not cancelled, refused or returned. The first shop costs the fast fee when a Deliver fast order is in
  the delivery, base + `ShipNowFee` (fast − base) when Ship now brought it forward (`Kind` `ShippedNow`), else the
  base fee (Don't hold alone = base). Each order stores `AddedFee` (what it added: base or fast, extra, the fast
  difference, kilograms, or 0 for a shop already in the delivery), and
  Create Order / Get Order return it as `fee`. The group total is never shown to a merchant.
- **Checkout quote** `GET /api/v1/quote?phone=&area=&line1=` (optional `areaId`, `line2`, `speed`, `doNotHold`,
  `pickupPointId` (default point), `weightGrams`):
  `{ fee, currency, joinsDelivery }`, the fee Create Order would give the same order now (৳60, +৳25, or 0 for a
  shop already in the delivery). Read only: creates no customer or address. `DeliveryGrouping.QuoteAsync` picks
  the group the same way Create Order does and shares its fee query.
- **Background jobs** (Hangfire, `Infrastructure/Jobs`): the recurring `lock-due-groups` (every 5 minutes,
  `Jobs:LockDueGroups`) queues `LockDueGroupsJob` once per active tenant; the runner sets the tenant from the
  job's parameter. Open groups past their deadline become `Locked` with `LockedOn` = the deadline. A new tenant job
  implements `ITenantJob`, is added to `TenantJobRegistry` and gets a recurring entry in `JobsSetup.ScheduleJobs`.
  Dashboard: http://localhost:5080/jobs (platform admin). `Jobs:Server` = false runs no server (integration tests).
- **Ship now** (`Application/Grouping/ShipNow`, `DeliveryGroup.ShipNow`): the signed-in customer closes an open
  delivery; it locks at once and is delivered the **next day** (`LocksAt` = the next tenant midnight). When that
  brings the day forward (Day 1) the delivery becomes `ShippedNow` and costs the fast difference (Dhaka +৳10; the
  button reads "Deliver tomorrow for +৳10"); on the last day to join it is free and stays `Waiting`. A shipped-now
  delivery still takes other shops' orders like a fast one. From the "My deliveries" page (each open delivery with
  its shops, last day to join, delivery day and a button) or `POST /api/v1/deliveries/{number}/ship-now` with the
  customer's sign-in cookie → `{ number, deliveryDate, addedFee }`;
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
- **At the door** (3.5, `Application/Delivery/Door/DoorHandler`, `TripVisits`): a rider's stop is a **visit**, every
  delivery on the trip for the same customer in the same area, with one fee (`DeliveryFeeCalculator.VisitFees`) split
  over their stops. The rider ticks refused orders, checks the amount (`/Rider?stop=DG-…&refused=OD-…`) and hands
  over only by confirming exactly what is due ("no fee, no handover"); `TripStop` records `Outcome`, `FeeCollected`,
  `CodCollected`. Nobody home: back to the hub (scan in, shelved again) for a free re-attempt another day; the second
  failed visit sends the orders to their shops. At Start trip an unready order moves to the customer's open delivery
  or a `FollowUp` delivery next day (first shop at the extra-shop fee; `Order.LeftBehindOn`). Refused parcels are
  scanned in ("Back to the shop") and handed back with the scan tab **Return to the shop**. The trip is `Finished`
  when every stop is done.
- **Paying at the door** (3.6a, `Domain/Payments`, `DoorHandler`): the customer pays the visit's fee and COD in
  **cash** (paid when the rider records it) or by **bKash / Nagad**: the rider asks the gateway (`IPaymentGateway`)
  for exactly the amount due and shows the QR; "Check payment and hand over" goes through only once the gateway says
  it is paid. One `Payments.Payment` per visit (`Fee` and `Cod` apart, rider and trip), linked from every stop
  (`TripStop.PaymentId`); a stop that collects money needs a paid payment. An unpaid QR is cancelled when the wallet,
  amount or outcome changes; one already paid is never dropped. `PaymentReceived` → outbox → SMS receipt (amount,
  method, delivery, fee, each order and its COD). The rider's **Today** shows how each stop was paid and the **cash
  to hand in**. The fake gateway's requests are paid by hand on `/Dev/Payments` (Development).
- **Not yet:** one-tap confirmation and advance payment (3.6b), ledger and settlement (3.7), a screen to add riders or change a bike's limit, a screen to change route times or weight settings, Ship now by SMS "reply 1" (needs an inbound SMS gateway), a screen for failed outbox messages,
  merchant screens to create API keys or enter orders manually, tenant admin screens.

### Database
- Schemas: `Platform` (Tenant), `Identity` (User, Role, UserRole, UserClaim, UserLogin, UserToken, RoleClaim),
  `Network` (Hub, Zone, Area, PickupRoute), `Customers` (Customer, CustomerAddress, PhoneOtp), `Merchants` (Merchant,
  MerchantApiKey, PickupPoint), `Orders` (Order, Package, OrderStatusHistory, sequence OrderNumber → `OD-100001`),
  `Grouping` (DeliveryGroup, sequence DeliveryGroupNumber → `DG-100001`; one `Open` group per customer + address
  by filtered unique index), `Delivery` (Rider, Trip — one per rider a day, TripStop — a delivery on one trip a
  day), `Notifications` (OutboxMessage), `Payments` (Payment: `Purpose` 1 Door, `Method` 1 Cash / 2 Bkash / 3 Nagad,
  `Status` 1 Pending / 2 Paid / 3 Cancelled; `Delivery.TripStop.PaymentId` points at it). `Order.DeliveryGroupId` is NOT NULL: every order travels in a group
  (`Scripts/Pre/001_GroupExistingOrders` grouped the orders saved before 2.2). `Order.AddedFee` is NOT NULL
  (`Scripts/Pre/002_PriceExistingOrders` priced the orders saved before 2.3). `Package.HubId`/`ReceivedOn` say
  where a parcel was last scanned in (null on the shuttle, when `ShuttleToHubId` says where it is going);
  `DeliveryGroup.Shelf` is unique per hub while set (`UX_DeliveryGroup_Hub_Shelf`). `Grouping.DeliveryGroup.Kind` 4 = FollowUp;
  `Delivery.TripStop.Outcome` (1 Delivered, 2 Refused, 3 NotHome), `FeeCollected`, `CodCollected`, `CompletedOn`;
  `Orders.Order.LeftBehindOn`.
- `Platform.Tenant` settings (fees, `GroupJoinDays`, time zone, currency, SMS sender, `WeightAllowanceGrams`,
  `ExtraKgFee`) have **no defaults**, in SQL or C#: every tenant states its own. No business value is hard-coded
  anywhere. The two weight settings are nullable in SQL (the launch seed 001 inserts tenants without them);
  `TenantCatalog` does not serve a tenant that has not set them. `Grouping.DeliveryGroup.Kind` (TINYINT, 1 Waiting,
  2 NextDay, 3 ShippedNow) was filled in for existing groups by `Scripts/Pre/003_DeliveryGroupKind`.
- Every tenant table: `TenantId` + FK + index; housekeeping columns `Archived`, `UpdatedId`, `UpdatedOn`, `Created`.
- Seeded by DbUp `2026/001_SeedLaunchTenants.sql`: **OneDrop Dhaka** (id 1, slug `dhaka`, 7 zones on 5 hubs,
  32 areas, ৳60 + ৳25, fast ৳70 since `2026/003_DhakaFastDeliveryFee.sql`) and **OneDrop Chattogram** (id 2, slug
  `chattogram`, 5 zones on 2 hubs, 14 areas, ৳70 + ৳30, fast ৳80). `2026/002_SeedPickupRoutes.sql` gives every
  launch zone a pickup route, staggered by `2026/005_StaggeredPickupTimes.sql` (Dhaka Uttara 11:00, Mirpur 11:30,
  Motijheel 12:00, Mohammadpur 12:30, Dhanmondi and Banani 13:00, Gulshan 13:30; Chattogram Halishahar 11:00,
  Nasirabad 11:30, Chawkbazar 12:00, Agrabad 12:30, Panchlaish 13:00). `2026/004_WeightAllowance.sql`: 2 kg per
  shop, then ৳15 (Dhaka) or ৳20 (Chattogram) per started kg. Roles are seeded by `Script.PostDeployment.sql`.
- Dev data in `OneDrop`: merchants 1–3 (Dhaka: Fashion House, Gadget BD, Beauty Shop) and 4–6 (Chattogram, same
  names); demo riders Rafiq Hasan (`rider@dhaka`, MIR, 30 parcels / 25 kg), Sumon Ali (`rider2@dhaka`, MIR, 12 /
  15 kg), Kamal Uddin (`rider3@dhaka`, GUL), Jamal Chowdhury (`rider@chattogram`, AGR). Orders OD-100001 onwards
  come from the live checks; each task's entry in the plan's daily log says which orders, phones and deliveries it
  made. Some delivery deadlines were moved into the past by hand for live checks (DG-100012, DG-100020,
  DG-100027–29), so those show as due today with no parcel at the hub. Orders before OD-100031 have no outbox rows.
  The Hangfire tables are installed at app start in both databases; only `OneDrop` runs jobs, as the integration
  tests start no job server. Group numbers have gaps: a sequence value used in a rolled-back dry run is not reused.

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
| Deliver fast / Don't hold → own group, `Locked` at once, `LocksAt` = next midnight (a `NextDay` delivery other orders can still join, 3.4a) | They never wait, so they must not take the customer's one `Open` slot |
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
| **Market review (2026-09-28):** Dhaka's fast fee ৳70; standard stays ৳60 + ৳25 | Couriers charge ৳55–70 for delivery within 24 h of pickup, which for a Facebook order is usually Day 3 anyway; fast is same day from pickup (about ৳105 at couriers). At ৳60 fast undercut waiting |
| From the review, done in 3.4a: fast, Don't hold and shipped-now deliveries (`DeliveryGroup.Kind`) take orders until the new order's pickup route has run on the delivery day; a new order joins the delivery that leaves soonest; Ship now adds the fast difference when it brings the day forward; 2 kg per shop, then per started kg (Dhaka ৳15, Chattogram ৳20); pickups 11:00–13:30, shuttle about 14:30, riders 17:00 | Density; a free Ship now would undo the fast price; every courier prices by weight; a 19:00 shuttle missed the Day 3 trip. A locked `Waiting` group takes nothing, so "an order on Day 3 starts a new group" still holds |
| New tenant settings are nullable in SQL when the launch seed cannot set them; the application refuses to serve a tenant without them | A NOT NULL column would break DbUp 001 on a fresh database, and a seed script is never edited after it has run |
| Planned from the review (3.5–3.8): shops pay a return charge per refused parcel and a late-handover fee per order left behind; the order left behind goes out next day at the extra-shop fee; advance payment only by risk (refusal, no-show, merchant's request), after a one-tap confirmation for new COD customers; one stop and one fee for the same phone and area on a trip | 20–30% of COD parcels come back and OneDrop earns only at the door; every customer is new at launch; a missed address match must never cost ৳60 + ৳60 |
| Done in 3.5: a rider's stop is a visit (same customer, same area, one trip) with one fee split over its deliveries; the rider hands over only by confirming exactly the amount due; nobody home = back to the hub and one free re-attempt, the second failure returns the orders; an unready order moves to the open delivery or a `FollowUp` (first shop at the extra-shop fee) and `LeftBehindOn` is recorded | "One stop, one fee" and "no fee, no handover"; the customer pays what was promised and the merchant's `AddedFee` never changes; 3.7 charges the shop from the records |
| 3.6 split with the owner (2026-09-29): 3.6a door payment and receipt (done), 3.6b one-tap confirmation and advance payment | Two changes, each tested and reviewed on its own |
| A wallet payment at the door is a gateway request for exactly the amount due, shown as a QR; the rider hands over only once the gateway says it is paid; the MVP gateway is a fake paid by hand on `/Dev/Payments` (owner) | "No fee, no handover" without trusting a customer's screen; a real bKash/Nagad adapter replaces the fake |
| One payment per visit, fee and COD kept apart, linked from every stop; money at a stop needs a paid payment (enforced in `TripStop.Complete`) | The ledger (3.7) splits COD per shop and the fee per delivery from the stops |
| An unpaid QR is cancelled when the wallet, amount or outcome changes; a paid one is never dropped (the rider hands over what it paid for) | No double charge and no refunds to handle in the MVP |
| For 3.6b (owner): an order waiting for its advance is not collected from the shop until paid; a customer with 10 accepted deliveries never pays in advance (tenant setting) | No parcel travels to an unpaid door; the two-way cost is what advance payment avoids |

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
| A new NOT NULL column on `Platform.Tenant` breaks a fresh database: DbUp `001_SeedLaunchTenants` inserts tenants without it and must not be edited | Make the setting nullable in SQL, set it for the launch tenants in a new DbUp script, and have `TenantCatalog` skip (and log) a tenant without it |
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
