# Project context — read this first

Everything someone new to this project needs to continue the work without asking: what we are building and why,
what exists, every decision taken and the reason for it, the environment, and the traps already found.

**Reading order for a new session:**
1. This file.
2. [Plans/Implementation-Plan.md](../Plans/Implementation-Plan.md) — the "Today" section says where we stopped and
   what is next; the daily log says what was done and tested.
3. [Conventions.md](Conventions.md) and [Database.md](Database.md) — the rules code and SQL must follow.
4. [Architecture.md](Architecture.md) for diagrams of how the parts fit, and [Demo.md](Demo.md) for the final demo.

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
| 3 | Operations and money | Group delivered, merchants settled | ✅ Done 2026-09-30 |
| 4 | Polish and proof | Full demo runs end to end | ✅ Done 2026-10-04: the final demo runs end to end ([Demo.md](Demo.md)) |
| 5 | After the MVP | The owner's list of gaps closed | 🔄 5.1–5.2 done (see the plan's Week 5) |

Task-level detail, the cut list, the job schedule, must-pass tests and the daily log are in
[Plans/Implementation-Plan.md](../Plans/Implementation-Plan.md). **That file is the source of truth for progress.**

---

## 3. What exists today (the complete 4-week MVP)

### Solution layout (`Courier.sln`)
| Project | Path | Contents |
|---|---|---|
| Domain | `src/Domain` | Entities and rules, no packages. `Common` (Entity with domain events, TenantEntity, Result/Error), `Platform/Tenant`, `Network` (Hub, Zone, Area, PickupRoute), `Customers` (Customer, CustomerAddress, PhoneNumber, PhoneOtp, CustomerStanding + CustomerStep + TrustRules), `Merchants` (Merchant with its webhook and shop window listing, MerchantApiKey, PickupPoint, DropOffRule, WebhookSignature), `Orders` (Order + state machine and scans, Package with its hub, PackageLabel, OrderStatusHistory, OrderStatusChanged), `Grouping` (DeliveryGroup + state machine, lock time and shelf, events), `Delivery` (Rider, Trip, TripStop, TripLoad, TripPlanner), `Pricing` (DeliveryFeeCalculator, FeeSchedule), `Payments` (Payment: cash or wallet, at the door or in advance, pending until paid; LedgerEntry: what each shop is owed or owes per order; Settlement: a payout), `Notifications` (OutboxMessage + retry rule) |
| Application | `src/Application` | Use cases as vertical slices: `Orders/CreateOrder`, `Orders/GetOrder`, `Orders/ConfirmOrder` (the SMS link: confirm, or pay the fee in advance), `Network/ListAreas`, `Network/PickupRoutes` (route list and sheet), `Network/HubScan` (collect, receive at a hub, shelves, shuttle load and manifest), `Orders/PackageLabels`, `Auth/PhoneLogin`, `Customers/CustomerDirectory` (find-or-create by phone/address), `Grouping/DeliveryGrouping` (join or open the order's group; quote), `Grouping/LockDueGroups` (the lock job), `Grouping/ShipNow`, `Grouping/CustomerDeliveries` ("My deliveries"), `Delivery/PlanTrips` (planner and job), `Delivery/HubTrips`, `Delivery/RiderDay` (the rider's stops, Start trip), `Delivery/Door` (at the door: amount due, QR, hand over, nobody home; writes the ledger), `Delivery/HubCash` (riders' cash hand-in), `Payments/SettleMerchants` (the payout job), `Payments/MerchantPayouts` (the shop's money), `Operations/Dashboard` (live counts per hub and packages per delivery), `Merchants/ShopDropOffs` (shops that bring their parcels to the hub), `Merchants/Webhook` (the shop's webhook settings and test), `Merchants/ShopWindow` (the shopping window and the shop's listing), `Pricing/GetQuote`, `Notifications` (outbox contracts, `SendOutbox` job and SMS texts, `SendWebhooks` job and signed bodies). Interfaces: `IAppDbContext`, `IOperationsFeed`, `ITenantJob`, `ITenantContext` (`TenantInfo.Fees`, `Trust`, `DropOff`), `ITenantCatalog`, `ICurrentUser`, `ISmsSender`, `IWebhookSender`, `IPaymentGateway`, `IPayoutGateway`, `ICustomerLinks`; `QueryFilters` (filter names) |
| Infrastructure | `src/Infrastructure` | `Persistence/AppDbContext` (EF Core, query filters), `Configurations/*` (mapping), `TenantSaveInterceptor`, `MultiTenancy` (TenantContext, TenantCatalog, CustomerLinks), `Identity` (AppUser, AppRole, claims), `Sms/FakeSmsSender`, `Payments/FakePaymentGateway` (paid by hand on `/Dev/Payments`) and `FakePayoutGateway` (payouts listed there), `Webhooks/HttpWebhookSender`, `Seeding/DemoDataSeeder`, `Jobs` (Hangfire setup, TenantJobRunner, TenantJobRegistry, OutboxDispatcher); `AppDbContext.SaveChangesAsync` writes the outbox |
| Web | `src/Web` | Razor Pages portals (merchant, customer, hub, operator admin, platform), `Live` (SignalR `OperationsHub`, `OperationsFeed`), `Labels/LabelQrCode` (QRCoder), `Api/V1` (orders, quote, areas by API key; deliveries by customer cookie), `Authentication/ApiKeyAuthenticationHandler`, `MultiTenancy` middleware, `Program.cs` |
| Database | `src/Database` | SQL project (Microsoft.Build.Sql 2.1.0) → `Database.dacpac`. Owns the schema |
| Database Update | `src/Database Update` | DbUp console (`dbup.exe`): data migrations in `Scripts/<Year>/`, data-loss scripts in `Scripts/Pre/` |
| Tests | `tests/Domain.Tests`, `tests/Architecture.Tests`, `tests/Integration.Tests` | 281 + 6 + 138 = **425 tests, all passing** |
| Tools | `tools/db/publish.ps1` | Deploys a database: `dbup pre` → dacpac publish → `dbup` (Windows PowerShell 5.1 and PowerShell 7 on Linux) |
| Simulator | `tools/Simulator` | Demo data (4.6): made-up shops per operator (a new API key each run), then orders from customers who buy at several shops, sent through the running app's quote and Create Order: `dotnet run --project tools/Simulator -- --orders 40 --pace 2` |
| Docker and CI | `Dockerfile`, `docker-compose.yml`, `.github/workflows/ci.yml` | Images `web` and `database` (runs `publish.ps1`); compose = SQL Server 2025 + deploy + app on `localhost:5080`, password in git-ignored `.env`; CI deploys a throwaway SQL Server from nothing and runs every suite (4.5) |

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
- **Confirmation and advance payment** (3.6b, `Domain/Customers/CustomerStanding`, `Application/Orders/ConfirmOrder`):
  when an order is placed, the customer's record across every shop (accepted deliveries, failed visits, refused orders)
  decides what it waits for (`Order.CustomerStep`, decided once and stored): **nothing** for a product paid online or a
  customer with `Tenant.TrustedAfterDeliveries` (10) accepted deliveries; **the fee in advance** after a refusal or
  no-show, or when the shop sends `feeInAdvance`; otherwise a **one-tap confirmation** for a customer who has never
  taken a delivery. The order's "placed" SMS asks instead of telling, with a link holding `Order.CustomerToken`
  (`ICustomerLinks` + `Links:PortalUrlFormat`). `/Customer/Order?token=…` needs no sign-in (the token is the key; an
  unknown one is a 404): "Yes, send it", or the delivery's first-shop fee with bKash / Nagad, the QR and "I have paid".
  An order waiting for its advance is **not collected** (`Order.Collect` refuses it; the route sheet says "Leave it" and
  leaves it out of the parcels to collect, and the merchant's list says why). One advance covers the delivery: an order
  joining it waits for the same payment, or for nothing once it is paid. At the door the paid advance comes off that
  delivery's share ("Already paid in advance"), so the fee is never collected twice. Create Order and Get Order return
  `waitsFor`.
- **Ledger and next-day payouts** (3.7, `Domain/Payments/LedgerEntry`, `Settlement`): what the operator owes each shop,
  one line per order and kind, written in the same save as its cause: the door adds each handed-over order's **COD**
  (from the visit's payment) and a **return charge** for each order going back to its shop (refused, or nobody home at
  the re-attempt); Start trip adds the **late-handover fee** for an order left behind that the shop had not handed over
  (still `Created`, not waiting for the advance). The fee stays in `Payments.Payment` (the operator's). The hourly
  `settle-merchants` job (`SettleMerchantsJob`) pays each shop every line not yet paid out up to yesterday (tenant's
  day) through `IPayoutGateway` (fake: to the shop's contact phone as its bKash, listed on `/Dev/Payments`; key
  `{slug}-settlement-{id}` so a retry never pays twice); a shop whose lines come to nothing or less is paid nothing and
  the lines wait for the next payout. Shops see it on **Payouts** (`/Merchant/Payouts`): next payout so far or "You
  owe", the lines, the latest payouts. A visit with nothing to pay (advance plus a product paid online) hands over with
  no door payment.
- **Riders' cash hand-in** (3.7, `Application/Delivery/HubCash`): hub **Cash** (`/Hub/Cash?hub=MIR`) lists each rider's
  cash collected today (and earlier trips not handed in); once every stop is done staff record what they received
  (`Trip.HandInCash`: `CashExpected`, `CashReceived`, once), "৳85 short". The rider's **Today** shows "Cash handed in".
  Payouts do not wait for it (owner).
- **Trust** (3.8, `Domain/Customers/CustomerStanding`, `Domain/Merchants/DropOffRule`, `Application/Merchants/ShopDropOffs`):
  no stored score, worked out from history when needed. **Customer:** after a refusal or no-show at any shop of the
  operator, the fee is paid in advance until they have accepted `Tenant.TrustedAgainAfterDeliveries` (3) deliveries
  since the last failure (dated by its stop's `CompletedOn`; a delivery handed over at the stop of a refused order does
  not count); trusted (never asked, even by the shop) after `TrustedAfterDeliveries` (10) deliveries with no failure
  since. **Shop:** `Order.FollowUpIn` records `ShopLateOn` when a rider leaves behind an order the shop had not handed
  over (the same rule as the late fee, which now reads it); `DropOffAfterLateHandovers` (3) of them within
  `LateHandoverWindowDays` (30) and the shop brings its parcels to the hub: the route sheet marks it "Brings its parcels
  to the hub until …" and counts nothing to collect there, the route list leaves it out, and the shop's **My orders**
  says until when and to which hub (its pickup points' zones' hubs, by the route's time). It is back on the route as
  soon as fewer late handovers are left in the window. The shop never learns why a customer pays in advance.
- **Finding your way** (UI upgrade, 2026-09-29): `/` sends each role to its home (shops "My orders", customers "My
  deliveries", riders "Today's trip", hub staff **Hub today** `/Hub`, tenant admins **Dashboard** `/Admin` (since
  4.1), platform admins Operators). The
  header marks the current page and shows who is signed in; Development adds a **Demo mode** bar (SMS inbox, wallet).
  Hub today shows the hub's day as six numbered steps with live counts, and a step bar sits on every hub page; the hub
  is picked once and remembered (`Pages/Hub/RememberHub`, host-only cookie). Pages open with a folded "how this works"
  box; statuses are words and colours from `Web/Display/Statuses`. Shops enter orders by hand on **New order**
  (`/Merchant/NewOrder`, the API's handler). Customers and riders see a tracker of where they are. `/Guide` follows
  one delivery through every screen; in Development the sign-in page offers the host's demo logins as buttons.
- **Live dashboards** (4.1, `Application/Operations/Dashboard`, `Web/Live`): counts worked out from the data when asked.
  Per hub: deliveries still open to shops, deliveries due out today (or late) and not out, parcels scanned in, parcels
  on the shuttle to it, riders out, and every parcel of a due delivery not on its shelf with where it is
  (`ParcelState.PlaceFor`). Hub today warns "N parcels for today's deliveries are not scanned in yet" with that list.
  Tenant admins' **Dashboard** (`/Admin`, policy `OperatorAdmin`) shows every hub and **packages per delivery** by area
  for 8 weeks of seven days ending today (`DeliveryDensity`, green at 2 or more). Live: after a committed save
  touching orders, packages, deliveries, trips, stops or riders, `AppDbContext` calls `IOperationsFeed`; the Web
  `OperationsFeed` sends "changed" (no data) to the operator's SignalR group (`/hubs/operations`, group from the
  sign-in's tenant claim) at most once a second, and `wwwroot/js/live.js` fetches the page again and swaps its
  `data-live` parts. One app instance (no backplane).
- **Merchant webhooks** (4.2, `Application/Notifications/SendWebhooks`, `Domain/Merchants/WebhookSignature`): every
  `Order.MoveTo` raises `OrderStatusChanged` → outbox `OrderStatusChangedMessage` (order, shop, status). `SendWebhooksJob`,
  run by the `OutboxDispatcher` in a loop of its own beside the texts, posts `{ type: "order.status_changed", timestamp,
  data: { number, externalReference, status } }` to `Merchant.WebhookUrl`, signed as Standard Webhooks with the shop's
  `whsec_` `WebhookSecret` (`webhook-id` `msg_{outbox id}`, `webhook-timestamp`, `webhook-signature`); a 2xx answer is
  sent, anything else retried like a text, and the shop is not called again in that run; no webhook → `Skipped`. The
  texts sender takes only `CustomerTexts.Types`. Shops set the address (https, plain http only to localhost), see and
  renew the secret and send a test on **Order updates** (`/Merchant/Webhook`); in Development `/Dev/Webhooks` plays
  their website and checks each signature. `HttpWebhookSender`: `Webhooks:Timeout`, no redirects.
- **Tenant isolation sweep** (4.3, `tests/Integration.Tests/TripTests.Isolation.cs`): reads every route the app maps
  from its endpoint table and holds one line per route saying what another operator, or another shop of the same
  operator, gets there, checked on a real delivery; **a new page or endpoint fails the sweep until it has its line**
  (the message names it). Every sign-in is also sent to every route of the other hosts (403), and the Dev pages are
  checked to be 404 outside Development.
- **Row-Level Security** (4.4, isolation layer 3): the SQL security policy `Platform.TenantIsolation` with predicate
  `Platform.TenantAccess` filters every table with a `TenantId` and blocks inserts and updates for another tenant.
  `Infrastructure/Persistence/TenantSessionInterceptor` marks each connection the context opens (`SESSION_CONTEXT`
  `TenantScoped` = 1 and `TenantId`, read only); a marked connection sees only its tenant's rows, and with no tenant
  only platform logins. Connections the app did not open (DbUp, SqlPackage, `sqlcmd`) are not restricted. A read that
  crosses tenants on purpose (API key lookup, the platform's Operators page, `/Dev/Webhooks`) wraps itself in
  `AppDbContext.AcrossTenantsAsync()` besides lifting the EF filter. `RowLevelSecurityTests` fails for a tenant table
  missing from the policy.
- **Combining deliveries and learning addresses** (4.7, `Application/Grouping/CombineDeliveries`): a customer with
  deliveries on their way (open or locked, never on a trip) to two addresses in the **same area** is asked "Same address
  as your delivery DG-…?" about the newer spelling: in the order's "placed" SMS (Create Order gives the order a link,
  `Order.AskCustomer`, when such a delivery exists), on the order page the link opens (`/Customer/Order?token=…`, now
  also for orders that wait for nothing) and on the card in "My deliveries". Worked out from the data when shown; no
  stored question. **Combine** (`DeliveryGroup.Combine`): the delivery leaving sooner goes on with every order
  (`Order.CombineInto`: its group and address, `AddedFee` unchanged, history "Delivery address confirmed by the
  customer"), the other is cancelled and hidden from the customer, its shelf passes to the kept one (two saves in one
  transaction, shelves being unique), its advance moves with it (`Payment.CoverInstead`); the cancelled spelling, and
  any spelling standing for it, gets `CustomerAddress.SameAsId` = the kept address, which Create Order and the quote
  follow, so the next order typed that way joins by itself. **Keep separate**: `KeptApartOn` on the newer address, never
  asked again. Not offered when both deliveries hold a shelf or both have an advance. Deliveries in other areas are never
  asked about.
- **Shopping window** (4.8, `Application/Merchants/ShopWindow`): a shop lists itself on **Shop window**
  (`/Merchant/Window`, `Merchant.ListInWindow`: the http(s) address customers shop at and an optional line on what it
  sells; **Leave the shop window** takes it out). `ShoppingWindow.ShopsAsync` lists the operator's listed shops by name,
  leaving out the ones given. The "joined" SMS of an order in an open delivery ends with "Add from any OneDrop shop for
  +৳25: …/Shops" when a listed shop is not in the delivery yet (`ICustomerLinks.Shops`); `/Shops` is open to everyone on
  the operator's host (404 on the platform's) and the same for everyone, so it names no customer or delivery; "My
  deliveries" shows each open delivery's window without the shops already in it (`CustomerDelivery.MoreShops`). No
  shop ever sees the window's customers.
- **API keys** (5.1, `Application/Merchants/ApiKeys`): a shop makes keys on **API keys** (`/Merchant/ApiKeys`;
  `MerchantApiKey.Create`, the plaintext shown once on the form's answer, never stored or kept in a cookie) and revokes
  them; a revoked key is refused on its next call. Another shop's key is not found.
- **Failed messages** (5.2, `Application/Notifications/FailedMessages`): tenant admins see the operator's texts and
  webhooks that are retrying or given up (`/Admin/Messages`): what each was about, to whom, tries, last error; a given-up
  one is sent again (`OutboxMessage.SendAgain`: pending at once, fresh attempts).
- **Not yet:** a screen to add riders or change a bike's limit, a screen to change route times or weight settings, Ship
  now by SMS "reply 1" (needs an inbound SMS gateway), tenant admin screens (Week 5 tasks 5.3–5.7 in the plan).

### Database
- Schemas: `Platform` (Tenant), `Identity` (User, Role, UserRole, UserClaim, UserLogin, UserToken, RoleClaim),
  `Network` (Hub, Zone, Area, PickupRoute), `Customers` (Customer, CustomerAddress with `SameAsId` and `KeptApartOn`, PhoneOtp), `Merchants` (Merchant
  with `WebhookUrl` and `WebhookSecret`, `chk_Merchant_Webhook`, `ShopUrl` and `ShopAbout`, `chk_Merchant_ShopWindow`; MerchantApiKey, PickupPoint), `Orders` (Order, Package, OrderStatusHistory, sequence OrderNumber → `OD-100001`),
  `Grouping` (DeliveryGroup, sequence DeliveryGroupNumber → `DG-100001`; one `Open` group per customer + address
  by filtered unique index), `Delivery` (Rider, Trip — one per rider a day, TripStop — a delivery on one trip a
  day), `Notifications` (OutboxMessage: `Status` 1 Pending / 2 Sent / 3 Failed / 4 Skipped), `Payments` (Payment: `Purpose` 1 Door / 2 Advance, `Method` 1 Cash / 2 Bkash / 3 Nagad,
  `Status` 1 Pending / 2 Paid / 3 Cancelled; `Delivery.TripStop.PaymentId` points at it; LedgerEntry: `Kind` 1 Cod /
  2 ReturnCharge / 3 LateHandoverFee, `Amount` negative for a charge, `UX_LedgerEntry_Order_Kind`, `SettlementId` once
  paid out; Settlement: `Status` 1 Pending / 2 Paid, `UpToDate`, `Account`). `Delivery.Trip.CashExpected`,
  `CashReceived`, `CashReceivedOn` hold the rider's cash hand-in. `Order.DeliveryGroupId` is NOT NULL: every order travels in a group
  (`Scripts/Pre/001_GroupExistingOrders` grouped the orders saved before 2.2). `Order.AddedFee` is NOT NULL
  (`Scripts/Pre/002_PriceExistingOrders` priced the orders saved before 2.3). `Package.HubId`/`ReceivedOn` say
  where a parcel was last scanned in (null on the shuttle, when `ShuttleToHubId` says where it is going);
  `DeliveryGroup.Shelf` is unique per hub while set (`UX_DeliveryGroup_Hub_Shelf`). `Grouping.DeliveryGroup.Kind` 4 = FollowUp;
  `Delivery.TripStop.Outcome` (1 Delivered, 2 Refused, 3 NotHome), `FeeCollected`, `CodCollected`, `CompletedOn`;
  `Orders.Order.LeftBehindOn`, `ShopLateOn` (`IX_Order_Tenant_ShopLateOn`); `Orders.Order.CustomerStep` (0 None, 1 Confirm, 2 PayInAdvance), `ConfirmedOn` and
  `CustomerToken` (`UX_Order_CustomerToken`).
- `Platform.Tenant` settings (fees, `GroupJoinDays`, time zone, currency, SMS sender, `WeightAllowanceGrams`,
  `ExtraKgFee`, `TrustedAfterDeliveries`, `ReturnCharge`, `LateHandoverFee`, `TrustedAgainAfterDeliveries`,
  `DropOffAfterLateHandovers`, `LateHandoverWindowDays`) have **no defaults**, in SQL or C#: every
  tenant states its own. No business value is hard-coded anywhere. The settings added after the launch seed are nullable
  in SQL (seed 001 inserts tenants without them; DbUp 004, 006, 007 and 008 set them); `TenantCatalog` does not serve a tenant that has
  not set them. `Grouping.DeliveryGroup.Kind` (TINYINT, 1 Waiting,
  2 NextDay, 3 ShippedNow) was filled in for existing groups by `Scripts/Pre/003_DeliveryGroupKind`.
- Every tenant table: `TenantId` + FK + index; housekeeping columns `Archived`, `UpdatedId`, `UpdatedOn`, `Created`;
  and a filter and two block predicates in the security policy `Platform.TenantIsolation` (function
  `Platform.TenantAccess`, 4.4).
- Seeded by DbUp `2026/001_SeedLaunchTenants.sql`: **OneDrop Dhaka** (id 1, slug `dhaka`, 7 zones on 5 hubs,
  32 areas, ৳60 + ৳25, fast ৳70 since `2026/003_DhakaFastDeliveryFee.sql`) and **OneDrop Chattogram** (id 2, slug
  `chattogram`, 5 zones on 2 hubs, 14 areas, ৳70 + ৳30, fast ৳80). `2026/002_SeedPickupRoutes.sql` gives every
  launch zone a pickup route, staggered by `2026/005_StaggeredPickupTimes.sql` (Dhaka Uttara 11:00, Mirpur 11:30,
  Motijheel 12:00, Mohammadpur 12:30, Dhanmondi and Banani 13:00, Gulshan 13:30; Chattogram Halishahar 11:00,
  Nasirabad 11:30, Chawkbazar 12:00, Agrabad 12:30, Panchlaish 13:00). `2026/004_WeightAllowance.sql`: 2 kg per
  shop, then ৳15 (Dhaka) or ৳20 (Chattogram) per started kg. `2026/006_TrustedAfterDeliveries.sql`: 10 accepted
  deliveries at both operators. `2026/007_MerchantCharges.sql`: return charge ৳30 (Dhaka) / ৳35 (Chattogram),
  late-handover fee ৳25 / ৳30. `2026/008_TrustAndDropOff.sql`: advance until 3 deliveries accepted since a failure;
  drop-off after 3 late handovers in 30 days (both operators). `2026/009_ShopLateOrders.sql` filled `ShopLateOn` from
  the late-handover fees already charged. `Customers.Customer.TrustScore` and `Merchants.Merchant.ReliabilityScore`
  (never used) were dropped by `Scripts/Pre/004_DropTrustScores`. Roles are seeded by `Script.PostDeployment.sql`.
- Dev data in `OneDrop`: merchants 1–3 (Dhaka: Fashion House, Gadget BD, Beauty Shop) and 4–6 (Chattogram, same
  names); demo riders Rafiq Hasan (`rider@dhaka`, MIR, 30 parcels / 25 kg), Sumon Ali (`rider2@dhaka`, MIR, 12 /
  15 kg), Kamal Uddin (`rider3@dhaka`, GUL), Jamal Chowdhury (`rider@chattogram`, AGR). Orders OD-100001 onwards
  come from the live checks; each task's entry in the plan's daily log says which orders, phones and deliveries it
  made. Some delivery deadlines were moved into the past by hand for live checks (DG-100012, DG-100020,
  DG-100027–29, DG-100045–46), so those show as due today with no parcel at the hub. The 3.7 live check dated its ledger
  lines a day back by hand to see the payout (settlements 1 and 2). The 3.9 demo run (DG-100065, OD-100100–OD-100102, Parveen Sultana 01819274111) was delivered and settled (settlements 3–5); the 3.8 check gave OD-100062 its `ShopLateOn` by
  hand and put Beauty Shop on drop-off (three late handovers, until 29 October). Orders before OD-100031 have no outbox rows, and orders before the 4.2 live check (OD-100108) no status-change rows. Dev Fashion House (Dhaka) has its webhook set to `http://localhost:5080/Dev/Webhooks` from the 4.2 live check. Dev
  Gadget BD and Beauty Shop (Dhaka) are listed in the shopping window from the 4.8 live check. The final demo (4.9: DG-100095, OD-100155–OD-100156, Taslima Rahman 01819274151) was delivered by Rafiq Hasan and settled (settlements 6 and 7); DG-100064 and DG-100069 were recorded nobody home on that trip.
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
| 4.4: RLS takes the tenant from `SESSION_CONTEXT`, set read only as each app connection opens; a marked connection without a tenant sees nothing; unmarked connections (DbUp, SqlPackage, `sqlcmd`) are not restricted; crossing tenants takes `AcrossTenantsAsync()` as well as `IgnoreQueryFilters` | Everything signs in as `sa`, so only the connection tells the app from the tools; the app fails closed while migrations and rebuilds see every row; one lifted EF filter alone is the bug layer 3 is for |
| 4.5: one Dockerfile (`web`, and `database` running `publish.ps1`); compose = SQL Server 2025 + deploy + the app as Development; CI starts a throwaway SQL Server with a per-run password and deploys from nothing | One deploy path everywhere; the compose stack is the demo; no stored secret, and every run proves a fresh database builds |
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
| Done in 3.6b: what an order waits for is decided once when it is placed, from the customer's record across every shop, and stored on the order (`CustomerStep`) | The shop hears the answer at once and it never changes under them; reading the record with the merchant filter lifted lets one shop's refusal protect the others without telling them why |
| The confirmation page is opened by the token in the SMS, with no sign-in; an unknown or used-up token is a 404, and the "placed" text itself asks | A customer who never visits OneDrop answers with one tap; a token names no customer, so a guessed link tells nobody anything, and one text per order is what they expect |
| One advance covers the delivery (a joining order waits for the same payment, or for nothing once paid), and a paid advance comes off the fee at that door | The advance is the first shop's fee, paid once; the customer must never pay the delivery fee twice, and 3.7 adds the advance row and the door row to the delivery's fee |
| SMS links come from `Links:PortalUrlFormat` + the tenant's slug, not from the current request | The outbox sends from a background job with no request, and the link must open the right operator's portal |
| 3.7 (owner): return charge ৳30 / ৳35, late-handover fee ৳25 / ৳30 (Dhaka / Chattogram); charges the day's COD does not cover carry to the next payout; shops are paid the next day even before the rider's cash is checked in | Market's low end; the late fee pays the second trip; a charge is never lost or invoiced; next-day payout must not slip |
| The ledger is per shop and per order (COD, return charge, late fee), written in the same save as the door or Start trip; the fee stays in `Payments.Payment` | A payout is exactly its lines, each traceable to an order and a payment |
| The return charge applies to refusals and to the second "nobody home"; the late fee only when the shop had not handed the order over, once per order | Both returns cost the same trip; a parcel already with the operator is the operator's delay |
| The settle job runs hourly, paying up to yesterday in each tenant's time zone; payouts are sent with an idempotency key | Tenants differ in time zone; a failed payout is retried within the hour and never paid twice |
| The riders' cash hand-in is recorded on `Delivery.Trip` once every stop is done | One trip per rider a day; no cash can come in after the last stop |
| 3.8 (owner, 2026-09-30): after a refusal or no-show the fee is paid in advance until 3 deliveries have been accepted since the last failure, and 10 deliveries count as trusted only with no failure since; a shop with 3 late handovers in 30 days brings its parcels to the hub until fewer are left in the window (tenant settings); the unused `TrustScore` and `ReliabilityScore` columns are dropped | A customer who refused once should earn their way back, and ten old deliveries must not excuse a new refusal; counts from history need no stored score, no nightly job and cannot drift; the drop-off ends by itself |
| A shop's late handover is stored on the order (`ShopLateOn`), not read from the ledger | It counts even at an operator whose late fee is ৳0, and it is dated for the window |
| 4.1: live dashboards hear only "changed" over SignalR and read themselves again through their own request, swapping their `data-live` parts | Nothing can leak through the socket; the page's authorisation and query filters decide what is shown; Razor markup is reused |
| Packages per delivery in seven-day weeks ending today, by area; tenant admins land on the operator's Dashboard | Bangladesh's week does not start on Monday and a week-start setting would exist for one report; the admin looks at the whole operator |
| 4.2: merchant webhooks are Standard Webhooks (`whsec_` secret, HMAC-SHA256 over `{id}.{timestamp}.{body}`), one per order status change, through the outbox; the body names the order and status only | Shops can use ready-made libraries; the outbox gives the same all-or-nothing save and retries as texts; a shop must never learn about the delivery |
| Webhooks have their own sender loop; a failing shop is not called again in that run; a shop with no webhook is skipped at send time | A slow shop server must not hold up texts or other shops; the save never reads shop settings |
| A webhook address is https (plain http only to localhost), no redirects; the secret is stored as it is | Encrypted in transit; a redirect could send our request elsewhere; signing needs the secret, unlike an API key |
| 4.3: the isolation sweep takes the routes from the running app and fails for a route with no line in it | A new page or endpoint cannot ship without its isolation check; a hand-kept list would drift |
| UI (2026-09-29): a home per role, "Hub today" with the hub's six steps and a step bar, a folded "how this works" box per page, the hub remembered in a host-only cookie; the **New order** form uses the API's handler; demo logins as buttons in Development | The owner could not tell where to click; one path for price and grouping; the cookie is a preference, each page still checks the hub |
| 4.7: two deliveries to addresses in one area are combined only when the customer says so; the newer spelling is the one asked about; the delivery leaving sooner goes on and the other spelling points at its address (`SameAsId`); "keep separate" is stored on the newer address | A guess could merge two flats in one building; nothing arrives later than promised; the alias makes the next order match by itself, and one answer settles the pair for good |
| Combining keeps each order's `AddedFee` (as for an order left behind) and is offered only for deliveries not yet on a trip, not both on shelves and not both with an advance | The merchant's fee never changes under it; a trip's visit already makes one stop of them; two shelves or two advances would need sorting out by hand |
| 4.8 (owner, 2026-10-04): a shop is in the shopping window only once it lists itself with its shop's address; "My deliveries" leaves out the shops already in that delivery; the SMS adds one line with a link to the operator's public `/Shops` page, which is the same for everyone | Nobody is advertised without agreeing, and a window needs somewhere to send the customer; a shop already in the delivery adds nothing; an SMS cannot hold a list, and a public page filtered by delivery would tell anyone guessing a delivery number which shops are in it |

---

## 5. Environment

| Item | Value |
|---|---|
| Machine | Windows 11, .NET SDK 10.0.4xx, PowerShell 5.1 + Git Bash |
| SQL Server | `ras-x2,1433`, SQL Server 2025, login `sa`. Shared with the team (DCN-&lt;name&gt; databases) |
| Databases | `OneDrop` (development), `OneDrop-Test` (integration tests). **Ask before creating any other** |
| Connection strings | `src/Web/appsettings.Local.json` (dev; also read by `publish.ps1` and passed to DbUp) and `tests/Integration.Tests/testsettings.Local.json` (test). Both git-ignored; format in the README |
| Tools | `sqlpackage` (dotnet global tool), `sqlcmd`, Docker Desktop 29.4 (start it first; the local compose stack keeps its data in the `onedrop_sql-data` volume), GitHub CLI at `C:\Program Files\GitHub CLI\gh.exe` (signed in as `talha33177net-eng`) |
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
docker compose up --build                         # the whole stack in Docker (.env from .env.example)
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
| A test that tries to write another tenant's rows really writes them when the isolation layer is broken (a mutation run renamed every Dhaka hub in `OneDrop-Test` to "Hijacked") | Isolation tests write each row's own value back (`SetProperty(h => h.Name, h => h.Name)`, `SET [Name] = [Name]`) and count the rows, so a failure changes nothing |
| `sys.security_predicates.operation_desc` reads `AFTER INSERT` with a space, not `AFTER_INSERT` | — |
| PowerShell reads `.ConnectionString` on a `DbConnectionStringBuilder` (a dictionary) as a key, so setting it parses nothing | Use `set_ConnectionString(...)` and `get_ConnectionString()` (`publish.ps1`) |
| Windows PowerShell 5.1's `Join-Path` takes two parts only, and PowerShell 7 on Linux has no `System.Data.SqlClient` | Build paths with `[System.IO.Path]::Combine`; parse connection strings with `DbConnectionStringBuilder` |
| Git Bash rewrites `/opt/...` arguments to `C:/Program Files/Git/opt/...` for `docker exec` | Prefix the command with `MSYS_NO_PATHCONV=1` |
| Visual Studio keeps `src/Database/Database.dbmdl` and `.jfm` locked while the solution is open | Leave them out when copying the repository (they are local caches) |
| Since Row-Level Security (4.4) SqlPackage refuses any table rebuild ("data motion … row level security", SQL71616), e.g. a column added before the housekeeping columns | `tools/db/publish.ps1` passes `AllowUnsafeRowLevelSecurityDataMovement` (with its other settings, no longer from the git-ignored `Local.publish.xml`, which a CI checkout lacks): safe, as the predicate does not restrict SqlPackage's unmarked connection, so every row is copied (checked by row counts on the 4.7 publish) |
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
| Razor's HTML encoder also turns `+` and `=` into `&#x2B;` and `&#x3D;`, so a base64 secret is not found as written in a page's HTML | Tests `WebUtility.HtmlDecode` the page before looking for it |
| `IHttpClientFactory` logs every request at Information, with a full stack trace for a refused connection | `System.Net.Http.HttpClient` is set to Warning in `appsettings.json`; `SendWebhooksJob` logs one line per failed webhook |
| The phone sign-in allows 10 requests a minute per address, and every test request comes from the same (no) address | A test that signs customers in sends `ClientAddressFilter.Header` (`X-Test-Client`) with an address of its own (`TripTests.CustomerCookieAsync`) |
| curl keeps no cookies for `*.localhost` hosts in its cookie jar, and in Development the sign-in page carries one anti-forgery token per demo-login button | For live checks, pass cookies in a `Cookie` header taken from `Set-Cookie`, and take the first token only |
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
