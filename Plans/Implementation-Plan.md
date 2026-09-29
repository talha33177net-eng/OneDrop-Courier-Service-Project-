# Implementation plan and progress tracker

The one place to see where we are and what comes next. Built from the two project PDFs
(*Implementation Plan* and *Project Documentation*, September 2026).

**Start of every session:** read [Today](#today), then take the first unchecked task of the current week.
**End of every task:** run the [test routine](#test-routine-after-every-task), tick the box, add a line to the
[daily log](#daily-log).

---

## Today

| | |
|---|---|
| Current week | **Week 3 — Operations and money** (7 of 11) |
| Next task | 3.6b Confirmation and advance payment (one-tap confirmation, merchant warning, advance by risk; decisions in the task) |
| Last session | 2026-09-29 — 3.4a committed on `day3` (`31a994f`, pushed); tasks 3.5 (delivery screen and attempts) and 3.6a (door payment) done and tested, both **uncommitted** for review. 3.4, the market review, 3.4a, 3.5 and 3.6a are not yet in `main` |
| Blockers | None |

---

## Test routine (after every task)

A task is **not done** until all of these pass. Record the result in the daily log.

1. **Build:** `dotnet build Courier.sln` shows 0 errors and no new warnings.
2. **New tests:** every new rule gets a unit test in `tests/Domain.Tests`. Anything touching the database, the
   API or tenancy gets an integration test in `tests/Integration.Tests`.
3. **Schema changed?** `./tools/db/publish.ps1` (dev) **and** `./tools/db/publish.ps1 -Database OneDrop-Test`.
4. **All suites green:**
   ```powershell
   dotnet test --project tests/Domain.Tests
   dotnet test --project tests/Architecture.Tests
   dotnet test --project tests/Integration.Tests     # must say succeeded, not skipped
   ```
5. **Live check:** `dotnet run --project src/Web`, then exercise the feature for real (curl the API or click through
   the page on `dhaka.localhost:5080`). Check a second tenant or merchant cannot see it.
6. **Update this file:** tick the task, note anything deferred, update [Today](#today).

---

## Status overview

| Week | Theme | Done when | Status |
|---|---|---|---|
| 1 | Foundation | An order can be created for a tenant | ✅ Done 2026-09-27 |
| 2 | Grouping core | 3 shops' orders form 1 group | ✅ Done 2026-09-28 |
| 3 | Operations and money | Group delivered, merchants settled | 🔄 7 of 11 |
| 4 | Polish and proof | Full demo runs end to end | ⬜ |

Tests today: **313 passing** (211 domain, 6 architecture, 96 integration).

---

## Week 1 — Foundation ✅

- [x] **1.1 Solution and projects.** `Domain`, `Application`, `Infrastructure`, `Web` (Clean Architecture), plus
      `Database` (SQL project, dacpac) and `Database Update` (DbUp), in DCN's style. `Courier.sln`.
- [x] **1.2 Tenancy.** Tenant catalog (cached); tenant from the subdomain or the API key; EF named query filters
      `Tenant` and `Merchant`; `TenantSaveInterceptor` stamps TenantId and blocks cross-tenant writes; a cookie
      from another tenant's host is refused.
- [x] **1.3 Domain entities and database.** Platform, Identity, Network, Customers, Merchants, Orders:
      21 tables in 6 schemas. `Order` state machine, `PhoneNumber` (E.164), address match key.
- [x] **1.4 Identity, roles, phone OTP.** 6 roles; staff sign in with email and password; customers sign in
      with an SMS code (5-minute expiry, 5 attempts, 3 codes per 15 minutes); fake SMS sender with an outbox page.
- [x] **1.5 Seed.** DbUp `001_SeedLaunchTenants`: Dhaka (7 zones, 5 hubs, 32 areas, ৳60 + ৳25) and Chattogram
      (5 zones, 2 hubs, 14 areas, ৳70 + ৳30). Development-only demo logins and 6 merchants with API keys.
- [x] **1.6 Merchant API key and Create Order.** `POST /api/v1/orders` (idempotency key, validation per field),
      `GET /api/v1/orders/{number}`, `GET /api/v1/areas`. Pages: landing, merchant orders, customer deliveries,
      platform tenants.

**Verified:** all 62 tests pass; live API run showed 201/200/409 idempotency, 404 for another merchant and
another tenant, one customer for two phone spellings, same phone = different customer per tenant.

**Carried forward from Week 1** (small gaps, fit them into later weeks):
- [x] Create Order returns the fee (৳60 / +৳25) → task 2.3 (this order's fee only; see 2.3).
- [ ] Merchant portal: issue and revoke API keys (only seeded keys exist today).
- [ ] Merchant portal: manual order form for Facebook sellers (the API is the only way in today).
- [ ] Tenant admin portal: zones, hubs, areas, merchants (today only through SQL/seed).

---

## Week 2 — Grouping core ✅

**Done when:** Fashion House, Gadget BD and Beauty Shop send orders for the same phone and address, and they
land in **one** delivery group that locks and shows ৳110.

Rules from the documentation this week must implement:
- Group = same **phone + address** (therefore same zone and hub), inside the 3-day window.
- The group opens with the first order. Orders on **Day 1 and Day 2** (tenant's `GroupJoinDays`, counted in the
  tenant's time zone) join it; it **locks at the end of Day 2** and is delivered on **Day 3**. The deadline is
  counted from the first order and **never moves**. An order on Day 3 starts a **new** group.
- **Ship now:** the customer can close the group early (app or SMS "reply 1").
- **Deliver fast** (next day at the tenant's fast fee, no waiting; the documentation said ৳60, Dhaka charges ৳70
  since the market review) and **Don't hold** items skip the group.
- Fee = `BaseDeliveryFee` + `ExtraShopFee` × (distinct **accepted** shops − 1); always calculated on what is
  actually delivered.
- Only **one open group per customer + address**, even with two orders at the same moment.

Tasks:
- [x] **2.1 Delivery group entity and table.** `Grouping.DeliveryGroup` (TenantId, CustomerId, AddressId, HubId,
      Number DG-…, Status Open/Locked/Dispatched/Delivered/Cancelled, OpenedOn, LocksAt, LockedOn, RowVersion).
      Filtered unique index: one `Open` group per (CustomerId, AddressId). `Orders.Order.DeliveryGroupId`.
      *Tests:* state machine unit tests; schema-match test covers the new table.
      *Done 2026-09-28:* `DeliveryGroup.Open` fixes `LocksAt` = midnight starting Day 3 in the tenant's time zone
      (UTC); `CanJoin(now)`; `MoveTo(status, now)` records `LockedOn`. `Order.DeliveryGroupId` is nullable for
      now: the four existing orders have no group. *Left for 2.2:* backfill them and consider making it NOT NULL;
      decide how Deliver fast groups carry their next-day date (they must not stay `Open`).
- [x] **2.2 Grouping rule in Create Order.** Find the open group or open a new one, inside the same save as the
      order. Deliver fast / Don't hold get their own single-order group. Concurrency: the unique index + retry.
      *Tests:* Day 1 and Day 2 join, Day 3 starts a new group (FakeTimeProvider); home vs office = 2 groups;
      parallel orders for a new customer = 1 group (integration).
      *Done 2026-09-28:* `Application/Grouping/DeliveryGrouping` saves the order and its group in one save. A
      waiting order joins the open group or opens one; an open group past `LocksAt` is locked on the spot
      (`LockIfDue`, `LockedOn` = `LocksAt`) so a new one can open before the lock job exists. Deliver fast and
      Don't hold get `DeliveryGroup.OpenAlone`: locked at once, delivered the next day. Losing the insert race
      to the unique index → join the winner's group. `Order.DeliveryGroupId` is NOT NULL; the orders saved
      before grouping were grouped by `Pre/001_GroupExistingOrders`. The API response does not mention the group
      (merchant privacy). Tenant setting defaults (৳60, ৳25, 2 days, time zone) removed from C# and SQL.
- [x] **2.3 Pricing.** One `DeliveryFeeCalculator` used everywhere (strategy per tenant settings). Create Order
      returns `fee` and `groupFee`. Every amount comes from the tenant's settings, never a constant in code.
      *Tests:* 1–4 shops give base, base + extra, base + 2 × extra, base + 3 × extra (Dhaka ৳60 / ৳85 / ৳110 /
      ৳135, read from the tenant); two orders from the same shop count once; Chattogram prices differ.
      Decide the fee for Deliver fast (`FastDeliveryFee`) and Don't hold orders.
      *Done 2026-09-28:* `Domain/Pricing/DeliveryFeeCalculator` with the tenant's `FeeSchedule`
      (`TenantInfo.Fees`): `GroupFee` = base + extra × (distinct shops − 1), counting only orders not cancelled,
      refused or returned; a Deliver fast group costs `FastDeliveryFee`; a Don't hold order alone costs the base
      fee (owner's choice). `AddedFee` = what one order adds to its group (base, extra, or 0 for a shop already
      in it). `Order.AddedFee` is set when the order is placed in its group (also after losing the open-group
      race) and saved with it; older orders were priced by `Pre/002_PriceExistingOrders`. Create Order and
      Get Order return **`fee` only**; **no `groupFee`** (owner's choice): the total would tell the merchant how
      many other shops the customer bought from. The group total is for the customer page (2.8) and the door
      (3.5), recalculated there on what is actually delivered.
- [x] **2.4 Quote endpoint.** `GET /api/v1/quote?phone=&area=&line1=` → "৳60" or "+৳25" for the checkout, without
      revealing which other shops are in the group (merchant privacy). *Tests:* quote matches the fee on create.
      *Done 2026-09-28:* `Application/Pricing/GetQuote` (query, validator, handler) and `Web/Api/V1/QuoteController`.
      Also takes `areaId`, `line2`, `speed` and `doNotHold`. Returns `{ fee, currency, joinsDelivery }`:
      `joinsDelivery` lets the checkout print "+৳25"; it tells no more than the fee already does, and nothing
      about which shops or how many. Read only: the customer and address are looked up by phone and match key,
      never created. `DeliveryGrouping.QuoteAsync` picks the group exactly as Create Order would (fast / Don't
      hold / unknown customer or address / open group past its deadline → a new delivery) and shares its fee
      query, so the quote and the order's `fee` cannot drift. A shop already in the delivery is quoted ৳0.
- [x] **2.5 Hangfire and the lock job.** Hangfire on SQL Server; jobs take the tenant as a parameter.
      `LockDueGroups` every 5 minutes. *Tests:* job locks due groups only, per tenant; an order after the lock
      opens a new group.
      *Done 2026-09-28:* Hangfire 1.8.25 on the application database; **Hangfire owns its `HangFire` schema**
      (created on first use; the dacpac publish keeps objects it does not know). `Application/Abstractions/ITenantJob`;
      `Application/Grouping/LockDueGroups/LockDueGroupsJob` locks each open group past `LocksAt` with `LockIfDue`
      (`LockedOn` = the deadline, however late), saving group by group and skipping one locked meanwhile.
      `Infrastructure/Jobs`: the recurring `lock-due-groups` (cron `Jobs:LockDueGroups`, `*/5 * * * *`) queues one
      `TenantJobRunner.RunAsync(job name, tenant id)` per active tenant, which sets the tenant on the job's scope.
      Jobs are stored by **name** (`TenantJobRegistry`), not as a generic method: Hangfire cannot load a generic
      method back from storage (found by the live check). `Jobs:Server` switches the server off (the test host).
      Dashboard at `/jobs`, platform admins only.
- [x] **2.6 Ship now.** Customer page button and API; locks the group immediately. *Tests:* only the owning
      customer can do it; a locked group rejects it.
      *Done 2026-09-28:* `DeliveryGroup.ShipNow(now, timeZone)` locks an open group at `now` and moves `LocksAt`
      to the next midnight in the tenant's time zone, so it is **delivered the next day** instead of on Day 3; a
      group already past its deadline is locked as due; any other status is a 409
      (`deliveryGroup.shipNow.notOpen`). `Application/Grouping/ShipNow/ShipNowHandler` finds the group by number
      **and** the signed-in customer (anyone else's is a 404) and treats a concurrent lock as the same 409.
      `POST /api/v1/deliveries/{number}/ship-now` (customer sign-in cookie on the tenant subdomain, policy
      `CustomerPortal`) returns `{ number, deliveryDate }`. "My deliveries" lists each open delivery (shops, last
      day to join, delivery day) with a **Ship now** button. The sign-in cookie now answers `/api` requests with
      401/403 instead of a redirect to the login page. *Left:* SMS "reply 1" needs an inbound SMS gateway (it will
      call `ShipNowHandler`); the SMS confirming the new delivery day is 2.7.
- [x] **2.7 Outbox and domain events.** `Notifications.OutboxMessage` saved in the same transaction; sender job
      every few seconds; SMS "joined your delivery", "group locked, arriving Day 3". *Tests:* event saved with the
      order; sender marks it sent; failure retries.
      *Done 2026-09-28:* entities raise `IDomainEvent`s (`Entity.Raise`); `Order.PlaceIn` raises
      `OrderPlacedInDelivery` (once, even when the open-group race places it twice) and `DeliveryGroup.LockIfDue` /
      `ShipNow` raise `DeliveryGroupLocked`. `AppDbContext.SaveChangesAsync` writes them to
      `Notifications.OutboxMessage` **in the same transaction**: save the changes (for the ids), add the outbox rows
      (`Application/Notifications/OutboxContracts`: `OrderPlacedMessage`, `DeliveryLockedMessage`, ids only),
      save, commit; a caller's transaction is joined; events are cleared only after commit. `SendOutboxJob` (a
      tenant job) sends up to 50 due messages oldest first, saving each as sent or failed; a failure waits 1, 2, 4,
      8 minutes and is `Failed` after 5 attempts. `CustomerTexts` writes the SMS from the data as it is when sent:
      an order in an open delivery ("… is in OneDrop delivery DG-…; other shops can join until the end of Tue 29
      Sep; we deliver on Wed 30 Sep"), an order travelling alone ("arriving on …"), a closed delivery (its shops
      and the day). Every 5 seconds (`Jobs:OutboxInterval`) the in-process `OutboxDispatcher` runs it for every
      tenant through `TenantJobRunner` (Hangfire's recurring jobs run at most once a minute); it runs only with
      `Jobs:Server`. *Left:* a second app instance could send a message twice (no claim on a row yet; one instance
      in the MVP); failed messages have no screen yet.
- [x] **2.8 Customer group page (PWA).** "My deliveries" shows the group: shops, packages collected, lock time,
      delivery day, fee so far, "You save ৳70" versus separate couriers. Web manifest so it installs on a phone.
      *Tests:* integration test for the page model; live check on a phone-width window.
      *Done 2026-09-28:* `Application/Grouping/CustomerDeliveries/CustomerDeliveriesHandler` returns the customer's
      deliveries on their way (open, locked, out with a rider; soonest first) and the 10 latest finished ones, each
      with its orders (shop, number, packages, status, COD), packages collected (orders past `Created`), the fee
      recalculated now with `DeliveryFeeCalculator`, the COD and `Savings`. `Order.IsForDelivery` (not cancelled,
      refused or returned) is now the one rule behind the fee, the COD due and the package count.
      `DeliveryFeeCalculator.Savings` = the tenant's base fee × distinct shops − the group fee, nothing for one shop
      (a separate courier is taken to charge the one-shop price). "My deliveries" is one card per delivery, built
      for a phone: status, delivery day, last day to join, orders, collected bar, fee so far, COD, total at the
      door, "You save ৳70 against 3 separate deliveries", what one more shop adds, Ship now. The old flat order
      table is gone (every order is in a delivery). PWA: `wwwroot/manifest.webmanifest` (start `/Customer`,
      standalone), SVG and 192/512 PNG icons, `sw.js` (never caches pages; shows `offline.html` without a signal),
      linked from the customer pages by `Shared/_CustomerApp` through a new layout `Head` section.
- [x] **2.9 Week 2 demo run.** The three Dhaka demo shops → one group → lock → ৳110. Update this file.
      *Done 2026-09-28:* quotes ৳60 / +৳25 / +৳25 equal the orders' fees; one group DG-100020 locked by the job at
      its deadline; the customer page shows ৳110 and "You save ৳70"; merchants and Chattogram see nothing of it
      (details in the daily log). *Carried to Week 3:* Ship now by SMS "reply 1", a screen for failed outbox
      messages, and the Week 1 portal gaps above.

---

## Week 3 — Operations and money ⬜

**Done when:** a group is picked up, sorted at the hub, delivered by a rider who collects the fee and COD, and
next morning each merchant is settled.

- [x] **3.1 Pickup routes and QR labels.** `Network.Route` per zone with a daily time (tenant setting); route
      stops = merchants with parcels; label `OD-100001-1` as a QR code on a printable page. *Tests:* route
      includes only that zone's merchants with waiting parcels.
      *Done 2026-09-28:* `Network.PickupRoute` (named so, not `Route`, to stay clear of ASP.NET's `[Route]`): one
      active route per zone (`UX_PickupRoute_Zone_Active`), `PickupTime` in the tenant's time zone with no default;
      DbUp `2026/002_SeedPickupRoutes` gives every launch zone a 2 PM run. `PickupRoute.NextPickup` = today's run
      until it has left, then tomorrow's. `Application/Network/PickupRoutes/PickupRoutesHandler`: the list (next run,
      stops, orders, packages per zone) and the sheet. A stop is a **pickup point** in the route's zone (the zone of
      the point's area, where the parcels physically are, not `Merchant.ZoneId`) with orders still `Created`,
      worked out when asked; Deliver fast and Don't hold orders are flagged "Next day". `Domain/Orders/PackageLabel`
      writes and parses `OD-100001-1` (for the 3.2 scanner). Pages: `/Hub/Routes` and the printable
      `/Hub/RouteSheet/{id}` (policy `Operations` = hub staff or tenant admin, nav "Pickup routes");
      `/Merchant/Labels` prints one QR label per parcel (QRCoder 1.8.0, inline SVG): hub code to sort to, label
      code, package x of n, recipient and area, shop, COD, "Next day"; no delivery data. Named orders, or every
      waiting order newest first, at most 100 (a notice says when older ones were left out). Linked from "My
      orders". *Left:* marking parcels collected at the merchant (`PickedUp`) comes with scanning in 3.2; the
      13:00 "build pickup routes" job is not needed while sheets are worked out when asked; no screen yet to
      change a route's time (SQL only, like zones).
- [x] **3.2 Hub scan-in and shelves.** Hub staff scan a label → order `AtHub`; each open group gets a labelled
      shelf. *Tests:* scanning another tenant's label is rejected; statuses move correctly.
      *Done 2026-09-28:* `Order.Collect` (collector at the shop: `Created` → `PickedUp`, whole order) and
      `Order.ReceiveAtHub(sequence, hub)` (per package: `Package.HubId` and `ReceivedOn`; the order is `AtHub` once
      every package has reached a hub; a parcel with no pickup scan passes through `PickedUp`); a repeated scan is
      `AlreadyRecorded`, a cancelled or dispatched order is refused. `DeliveryGroup.Shelf`: a delivery waiting at its
      own hub (open or locked) takes the **lowest free shelf number** there at its first parcel, labelled
      `MIR-01` (`DeliveryGroup.ShelfCode`), and frees it when dispatched, delivered or cancelled
      (`UX_DeliveryGroup_Hub_Shelf`). A parcel scanned in at another hub is told "Send on to GUL" and takes no
      shelf. `Application/Network/HubScan/HubScanHandler` parses with `PackageLabel.TryParse`; another operator's
      label is "not found" (query filter); two scans at once (same order, or two deliveries taking one shelf) lose
      on the row version or the unique index and scan again from fresh rows (up to 5 times). Pages (policy
      `Operations`, nav "Scan" and "Shelves"): `/Hub/Scan?hub=MIR` with tabs **Receive at the hub** and **Collect
      at the shop**, a box a hand scanner types into, a camera button where the browser has `BarcodeDetector`,
      and a large answer (shelf, send on, or why not); `/Hub/Shelves?hub=MIR` lists each shelf's delivery, day,
      orders and "parcels here of total", Ready once locked and complete. *Left:* the parcels to send on are
      listed by 3.3; a parcel coming back from a rider (not home) is 3.5; collecting is per order, not per package;
      the camera scan is untested on a real phone.
- [x] **3.3 Hub shuttle.** Parcels picked up in another zone travel to the customer's zone hub. *Cut option:*
      a manual "transfer" button.
      *Done 2026-09-28 (not cut):* a scan at each end. `Order.LoadForShuttle(sequence, hub, toHub)`: a package
      scanned in here whose delivery leaves from another hub goes on the shuttle (`Package.HubId` null,
      `ShuttleToHubId` = the delivery's hub); a package not scanned in here, one delivered from this hub ("it goes on
      its shelf"), or one of a cancelled order is refused; loading again is `AlreadyRecorded`. The existing receive
      scan at the other end clears `ShuttleToHubId` and shelves the delivery. The order stays `AtHub` in transit;
      "all packages in" now reads `ReceivedOn`, so a package on the shuttle still counts. `HubScanHandler.LoadAsync`
      and `ShuttleAsync`: the manifest per hub, parcels to load grouped by destination hub (next-day parcels, then
      the soonest delivery day, first; cancelled, refused and returned left out) and parcels on their way in. Pages:
      a third scan tab **Load the shuttle** (`/Hub/Scan?hub=MIR&mode=Load`, answer "Load for GUL hub") and the
      printable `/Hub/Shuttle?hub=MIR` (nav "Shuttle"). No 19:00 job and no shuttle time setting: staff load when
      the shuttle leaves, from the manifest. *Left:* a parcel whose order is cancelled while it is on the shuttle
      cannot be scanned in at the other end; sending parcels back to the shop comes with returns (3.5).
- [x] **3.4 Riders and trips.** `Delivery.Rider`, `Delivery.Trip`; the plan-trips job assigns Day 3 groups to
      riders; per-bike limit. Rider PWA screen: today's stops.
      *Done 2026-09-28:* schema `Delivery`: `Rider` (hub, login `UserId`, name, phone, and the bike's limit
      `MaxParcels` and `MaxWeightGrams`, set per rider with no default), `Trip` (rider, hub, `DeliveryDate` in the
      tenant's days, `Planned` → `Out`; one per rider a day, `UX_Trip_Rider_DeliveryDate`) and `TripStop` (a delivery
      on a trip, the trip's date copied so `UX_TripStop_DeliveryGroup_DeliveryDate` keeps a delivery on one trip a
      day). `Domain/Delivery/TripPlanner` (pure): deliveries already late first, then area by area so a rider's stops
      are neighbours, each to the first rider (by name) whose bike still has room on parcels and weight; one too big
      for any bike's room waits. `Application/Delivery/PlanTrips/TripPlanning` plans each hub: due = `Locked` with
      `LocksAt` passed (delivery day has started) and at least one parcel on its shelf; a rider takes deliveries until
      they leave, so running again adds only what has become ready; a trip still `Planned` after its day is
      `Cancelled` and its deliveries go out today; each stop is saved on its own and one taken by a planner running
      at the same moment is skipped. `PlanTripsJob` (tenant job) runs it every 15 minutes (`Jobs:PlanTrips`), not
      once at 08:00 (see the decisions log). **Start trip** (`RiderDayHandler.StartAsync`): every order whose parcels
      are all on the shelf is handed over (`Order.HandToRider`: `OutForDelivery`, packages off the hub), its delivery
      becomes `Dispatched` and frees its shelf; an order not ready stays at the hub; a delivery with nothing ready
      comes off the trip and stays `Locked`; nothing ready at all is refused. Pages: hub **Trips**
      (`/Hub/Trips?hub=MIR`, policy `Operations`): each rider's deliveries against the bike (parcels here while
      planned, parcels taken once out), the deliveries due today on no trip, and **Plan trips now**; rider **Today**
      (`/Rider`, policy `Rider`, the landing page for riders): stops by area with recipient, address, landmark, phone,
      shelf, each shop's labels ("On the shelf" / "Not all here yet"), the fee on what is taken and COD, the total to
      collect, and Start trip; installs as a phone app (`rider.webmanifest`, `Shared/_RiderApp`). Demo riders (dev
      only): two Mirpur bikes of different sizes, one Gulshan, one Agrabad. *Left:* no screen yet to add riders or
      change a bike's limit (SQL only, like zones); a stop added by the planner at the very moment the rider starts
      could land on the trip that just left (it goes out on the next day's trip); parcels are handed over without a
      scan per parcel; what happens to an order left behind when its delivery went out (a later delivery at +৳25)
      is 3.5; no customer SMS for "out for delivery" yet (3.6 sends the receipt).
- [x] **3.4a Market pricing** (added by the market review of 2026-09-28, see the decisions log).
      - **Fast and Don't hold deliveries can be joined.** A delivery leaving tomorrow takes the customer's other
        orders to the same address while the new order's pickup route still runs before that day's trips (its next
        run falls on the delivery day, `PickupRoute.NextPickup`). A new order joins the delivery that leaves soonest
        and can still take it, at the extra-shop fee. The one-open-group rule for Day 3 deliveries is unchanged.
      - **Ship now is an upgrade to fast.** When it brings the delivery day forward it adds the tenant's fast fee
        minus its base fee (Dhaka +৳10), shown on the button ("Deliver tomorrow for +৳10") and counted in the fee
        at the door; on Day 2 it moves nothing and stays free.
      - **Weight allowance per shop.** New tenant settings: the weight included per shop in a delivery (Dhaka 2 kg)
        and a fee per started kg above it (Dhaka ৳15–20, to confirm). One calculator for Create Order, the quote
        (optional weight) and the door. New NOT NULL tenant columns go through `Scripts/Pre`.
      - **Daily timetable.** Pickup routes staggered 11:00–13:30 (farthest zones first), the shuttle right after
        scan-in (about 14:30), riders out at 17:00, so every order placed by the end of Day 2 reaches its hub by
        16:00. Route times are data (a DbUp script; times to confirm with the owner); the shuttle manifest lists
        parcels for today's trips first.
      *Tests:* a second shop joins a fast delivery before its pickup route leaves on the delivery day and opens a
      new one after; the quote says the same; Ship now on Day 1 adds the difference and on Day 2 nothing; a 3 kg
      shop pays one extra kg in Dhaka and Chattogram uses its own settings.
      *Done 2026-09-29:* `Grouping.DeliveryGroup.Kind` (`DeliveryGroupKind`: `Waiting`, `NextDay` for a group opened by
      a Deliver fast or Don't hold order, `ShippedNow` for one brought forward by Ship now; `Pre/003` filled it in for
      existing groups, sent-early ones as `Waiting` so nobody is charged afterwards). `DeliveryGroup.CanTake(now,
      pickup, waits)`: an open group before its deadline; a locked `NextDay`/`ShippedNow` delivery not yet out while
      the order's pickup (`PickupRoute.NextPickup` of its pickup point's zone) falls on or before its delivery day; an
      order that must not wait (fast, Don't hold) only a delivery arriving by tomorrow, so on Day 2 it also joins the
      open group. `DeliveryGrouping` puts the order in the candidate that leaves soonest (lowest `LocksAt`, then id),
      else opens a group or a next-day delivery; the quote chooses the same way and now takes the pickup point
      (default point when not given). A locked `Waiting` group still takes nothing, so an order on Day 3 starts a new
      group. **Fee:** the first shop costs the fast fee when a fast order is in the delivery, base + `ShipNowFee`
      (fast − base, never negative) when shipped now, else the base fee; + extra-shop fee per other shop; + each
      started kg a shop's orders weigh above `Tenant.WeightAllowanceGrams`, at `Tenant.ExtraKgFee`. "You save" leaves
      the weight out (separate couriers charge it too). Dhaka 2 kg + ৳15, Chattogram 2 kg + ৳20 (DbUp
      `2026/004_WeightAllowance`, owner's values); the columns are **nullable** in SQL because the launch seed (001)
      inserts tenants without them and must not be edited, and `TenantCatalog` does not serve a tenant without them
      (warning in the log). **Ship now:** `ShipNowBringsForward` → the delivery becomes `ShippedNow`; the API returns
      `addedFee`; "My deliveries" shows "Deliver tomorrow for +৳10" (free "Ship now" on Day 2). Rider stops and the
      customer page price by the same calculator with weights and kind. **Timetable:** DbUp
      `2026/005_StaggeredPickupTimes` (owner's times): Dhaka Uttara 11:00, Mirpur 11:30, Motijheel 12:00, Mohammadpur
      12:30, Dhanmondi and Banani 13:00, Gulshan 13:30; Chattogram Halishahar 11:00, Nasirabad 11:30, Chawkbazar
      12:00, Agrabad 12:30, Panchlaish 13:00. The shuttle manifest lists the soonest delivery day first, then next-day
      deliveries; the manifest, route sheet and scan answer mark "Next day" by the delivery's kind (so an order that
      joined a fast delivery is marked), while the merchant's labels keep the order's own flag (merchant privacy).
      *Left:* an order joining a next-day delivery at the moment its rider presses Start trip is left behind with it
      (3.5 sends orders left behind the next day); an order for a delivery due today whose parcels arrive after the
      riders left is 3.5's too; no screen yet to change weight settings or route times (SQL only).
- [x] **3.5 Delivery screen and attempts.** Handover only after the fee is paid ("no fee, no handover"); refuse
      one parcel (fee counts accepted shops only); not home = one free re-attempt, then return.
      *Added by the market review:*
      - **One stop, one fee.** Deliveries for the same phone in the same area on one trip are one stop for the
        rider and one fee over every shop handed over, so a missed address match never costs ৳60 + ৳60.
      - **An order left behind** (shop not ready, or not all its parcels at the hub at Start trip) goes on a
        follow-up delivery the next day, or joins the customer's open delivery at that address. The customer pays
        only the extra-shop fee for it (the Day 3 fee already counted only what was delivered); the shop is
        charged the late-handover fee (3.7).
      - **A refused parcel** goes back to its shop, which is charged the return charge (3.7).
      *Done 2026-09-29:* **Visits.** The rider's stops are visits (`Application/Delivery/TripVisits`): every delivery
      on the trip for the same customer (phone) in the same area is one stop, named by its first delivery's number,
      with one fee: `DeliveryFeeCalculator.VisitFees` prices the visit as one delivery (from its dearest kind: shipped
      now, then waiting or next day, then follow-up) and splits it over the deliveries so each `TripStop` records its
      part. **At the door** (`Application/Delivery/Door/DoorHandler`, trip out): the rider ticks the orders the
      customer refuses and checks the amount (a GET, `/Rider?stop=DG-…&refused=OD-…`: "Collect ৳1,260", fee on what
      is taken + its COD); "Collected ৳X, hand over" posts the amount and is refused unless it equals what is due now
      ("no fee, no handover"; 3.6 adds how it was paid). Taken orders `Delivered`, refused ones `Refused`, each delivery
      `Delivered` (or `Cancelled` when nothing of it was taken); `TripStop.Outcome` (`Delivered`, `Refused`,
      `NotHome`), `FeeCollected`, `CodCollected`, `CompletedOn`. **Nobody home:** orders back to `AtHub` (parcels with
      the rider until scanned in, which shelves the delivery again), delivery `Locked`, planned again on another day for
      free (the planner skips a delivery already on a trip today); at a delivery's second failed visit its orders are
      `Refused` and it is `Cancelled`. The trip is `Finished` when every stop is done; the page shows collected and
      still to collect. **Left behind:** at Start trip an order not ready of a delivery that goes out moves
      (`Order.FollowUpIn`, `LeftBehindOn` recorded for 3.7, `AddedFee` unchanged, the customer is texted) to the
      customer's open delivery to that address, or a new `FollowUp` delivery (`DeliveryGroupKind` 4: locked, next day,
      first shop at the extra-shop fee, joinable), which takes over the freed shelf when its parcels are already there.
      **Returns:** a refused parcel scanned in at a hub keeps `Refused` and is answered "Back to the shop Gadget BD";
      the scan tab **Return to the shop** (`Order.ReturnToMerchant`) hands it back (`ReturnedToMerchant`). "My
      deliveries" shows a handed-over delivery's collected fee. *Left:* returns are not on the pickup route sheet yet
      and a return from another zone's hub has no shuttle back (staff carry it); no SMS for "nobody home" yet (3.6
      sends texts at the door); the rider hands in the day's cash in 3.7; a visit's stops for deliveries in other
      areas are not merged (by design, 4.7).
- [x] **3.6a Door payment** (3.6 split in two with the owner, 2026-09-29). Collect fee + COD, cash or QR (bKash/Nagad
      adapter, fake in MVP). SMS receipt.
      *Done 2026-09-29:* schema `Payments`, table `Payment` (`Domain/Payments`): customer, trip, rider, the visit's first
      delivery, `Purpose` (1 Door), `Method` (Cash, Bkash, Nagad), `Status` (Pending, Paid, Cancelled), `Fee` and `Cod`
      kept apart for the ledger, the gateway's reference and payment link, `PaidOn`. Cash is paid when the rider records
      it; a wallet payment is `Pending` until the gateway says it is paid. `Delivery.TripStop.PaymentId`: every stop of a
      visit points at its payment, and `TripStop.Complete` refuses money without a paid payment ("no fee, no handover"
      in the domain). `Application/Abstractions/IPaymentGateway` (`RequestAsync`, `IsPaidAsync`) with
      `Infrastructure/Payments/FakePaymentGateway` and the dev page `/Dev/Payments` where a request is paid by hand.
      `DoorHandler.RequestQrAsync` asks the wallet for exactly the amount due and shows the same QR on a second ask;
      `HandOverAsync` takes the method: cash, or the QR shown once the gateway has the money (`door.qr.unpaid`, and
      `door.qr.none` for a wallet with no QR). An unpaid QR is cancelled when the wallet, the amount or the outcome
      changes; one the customer paid after all is never dropped (`door.qr.paid`: hand over what it paid for).
      `PaymentReceived` → outbox `PaymentReceivedMessage` → SMS receipt: amount and method, delivery, fee and each order
      handed over with its COD. Rider **Today**: "Cash ৳X collected, hand over", **bKash QR** / **Nagad QR**, the QR
      with "Check payment and hand over", each done stop "Delivered, paid by bKash", and **Cash to hand in**.
      *Left:* a wallet payment for orders then refused is not refunded (the rider must hand over what was paid for);
      the customer's page does not say how a delivery was paid (the receipt does); the rider's cash hand-in at the hub
      is 3.7; a real gateway's callback is not modelled (the rider checks); no phone-width screenshot of the QR yet.
- [ ] **3.6b Confirmation and advance payment.** Owner's answers (2026-09-29): an order waiting for its advance is
      **not collected** from the shop until paid (the route sheet shows "Waiting for the advance" and skips it, and so
      does the merchant's list); a customer with **10 accepted deliveries** never pays in advance (a tenant setting,
      Dhaka and Chattogram 10). Advance payment, narrowed by the market review from the documentation's "new and
      low-trust customers pay the fee in advance by payment link":
      - **A new COD customer confirms with one tap** on an SMS link (opens "My deliveries"); the merchant is warned
        when an order is still unconfirmed before its pickup.
      - **The fee is paid in advance** (bKash payment link) only after a refusal or no-show (3.8), or when the
        merchant asks for it on the order; never for a product already paid online; never for a customer with
        enough accepted deliveries (tenant setting). The advance is the first shop's fee; extra shops are paid at
        the door; it is kept when everything is refused.
- [ ] **3.7 Ledger and next-day settlement.** Split every payment per merchant; the settle job pays yesterday's
      COD to each merchant (fake bKash/bank). Rider end-of-day cash deposit and check. *Tests:* ledger balances;
      each merchant gets exactly its COD.
      *Added by the market review:* the payout deducts the **return charge** per refused parcel and the
      **late-handover fee** per order left behind (tenant settings; Dhaka ৳30–40 return charge, to confirm).
      Merchants pay nothing for a delivered order, and COD handling stays free (Dhaka couriers charge 0–1%).
      Next-day payout must not slip: Pathao pays daily.
- [ ] **3.8 Trust score (simple).** Refusals and no-shows lower it; merchants' late handovers lower theirs.
      *Cut option:* a simple refusal counter.
      *Added by the market review:* a customer's refusal or no-show switches on advance payment (3.6) at every shop
      of the operator; a shop learns only "pays the fee in advance", never why or where. A shop late again and
      again must bring its parcels to the hub.
- [ ] **3.9 Week 3 demo run.** Group delivered, merchants settled.

---

## Week 4 — Polish and proof ⬜

**Done when:** the final demo script below runs end to end.

- [ ] **4.1 Dashboards (SignalR).** Live counts for admin and hub: open groups, parcels at hub, riders out.
      *Added by the market review:* **packages per delivery per area, week by week** (the number the business
      lives on), and a hub warning before the riders leave: "3 parcels for today's deliveries not scanned in yet".
- [ ] **4.2 Merchant webhooks.** Order status changes posted to the merchant's URL through the outbox, signed.
- [ ] **4.3 Tenant isolation test sweep.** Every endpoint and page: tenant A gets 404 for tenant B; merchant sees
      only its parcels.
- [ ] **4.4 SQL Server Row-Level Security** (isolation layer 3), using SESSION_CONTEXT set per connection.
      *Cut option:* moves to "next step"; EF filters and the save interceptor still protect the data.
- [ ] **4.5 Docker and CI.** `docker-compose.yml` (app + SQL Server); GitHub Actions: build, publish the dacpac
      to a throwaway database, run all tests.
- [ ] **4.6 Simulator.** `tools/Simulator` fills both tenants with fake merchants and orders for demos.
- [ ] **4.7 Combine deliveries and learn addresses** (market review). An order with the same phone and area as an
      open delivery but another address match key asks the customer by SMS and on "My deliveries": "Same address as
      your delivery DG-…? Combine / Keep separate". Combining merges the deliveries and recalculates the fee; the
      other spelling is saved as an alias of the address, so the next order matches by itself. Deliveries in
      different areas are never combined (home and office stay apart).
- [ ] **4.8 The open delivery as a shopping window** (market review). The "joined" SMS and "My deliveries" say
      "Your delivery is open until Tuesday: add from any OneDrop shop for +৳25" with the operator's partner shops.
      The list shows every shop, never the ones this customer bought from.
- [ ] **4.9 README, diagrams, demo.** Final documentation and a recorded demo run.

### Final demo script
1. Log in to OneDrop Dhaka.
2. Shop A and Shop B send orders for the same phone through the API.
3. The customer sees one group and taps **Ship now**.
4. The hub scans and shelves the parcels.
5. The rider delivers and collects ৳85 + COD.
6. Next morning, both merchants are settled.
7. Switch to OneDrop Chattogram: same phone number, completely separate data and its own prices.

---

## If we fall behind — cut in this order

1. Hub shuttle becomes a manual "transfer" button.
2. Trust score becomes a simple refusal counter.
3. Row-Level Security moves to "next step".

Payments stay fake in the MVP either way.

## Daily background jobs (target schedule)

| Time | Job | Week |
|---|---|---|
| Every 5 seconds (`Jobs:OutboxInterval`) | Outbox sender (in-process dispatcher, not Hangfire) | 2 ✅ |
| Every 5 minutes | Lock check (lock due groups) | 2 ✅ |
| 02:00 | Recalculate trust and reliability scores | 3 |
| 06:00 | Settle merchants (yesterday's COD) | 3 |
| Every 15 minutes (`Jobs:PlanTrips`) | Plan delivery trips (deliveries due today → riders), instead of once at 08:00 (3.4) | 3 ✅ |
| 13:00 | Build pickup routes per zone — not needed: the route sheet is worked out when opened (3.1) | 3 ✅ |
| 11:00–13:30 | Pickup route runs (merchants → hub), staggered by zone, farthest first (DbUp 005, 3.4a) — no job: the route sheet | 3 ✅ |
| About 14:30 | Hub shuttle (zone → customer's hub), right after scan-in, not 19:00 (market review) — no job: staff load it from the manifest (3.3) | 3 ✅ |
| 17:00 | Riders leave; delivery 5–9 PM | 3 |
| 23:59 | Groups lock (Day 2 deadline) | 2 |

## Must-pass tests (from the plan)

| Test | Status |
|---|---|
| An order on Day 3 starts a new group | ✅ `DeliveryGroupingTests` |
| Fee = base + extra per distinct accepted shop (tenant settings; Dhaka ৳60 + ৳25) | ✅ `DeliveryFeeCalculatorTests`, `PricingTests` |
| Only one open group per customer + address, even with two orders at the same moment | ✅ `DeliveryGroupingTests` (race forced and recovered) |
| Tenant A gets 404 for tenant B's order | ✅ `OrderApiTests` |
| A merchant sees only its own parcels | ✅ `OrderApiTests` |
| Every entity (except Platform) has a TenantId — build fails otherwise | ✅ `TenantOwnershipTests` |

---

## Decisions log

| Date | Decision | Why |
|---|---|---|
| 2026-09-27 | The schema is owned by a SQL project + DbUp, as in DCN; EF Core only maps onto it (no EF migrations) | Easy to maintain, same workflow as DCN. `SchemaMatchesModelTests` catches drift |
| 2026-09-27 | `Zone.HubId`: a zone is served by one hub, a hub can serve several zones | 7 Dhaka zones on 3–5 hubs, as the MVP scope requires |
| 2026-09-27 | Tenant from subdomain (portals) or API key (API); tenant staff sign in on their own subdomain | Host-only cookies keep tenants apart; login-based tenant only needed for a future rider app |
| 2026-09-27 | No product-name prefix in code (`src/Web`, `AppDbContext`, `Courier.sln`) | User preference, DCN style. The brand stays in UI text and data |
| 2026-09-27 | Integration tests use `OneDrop-Test` on ras-x2 (`testsettings.json`) | Must-pass tests need real SQL Server; test data stays out of `OneDrop` |
| 2026-09-28 | A group's `LocksAt` is the first moment of delivery day (tenant midnight, stored UTC), fixed at opening; an order joins when `now < LocksAt` | One exclusive boundary instead of "11:59 PM": no gap second, and the lock job and the join rule use the same value |
| 2026-09-28 | Group status Dispatched → Locked is allowed (customer not home, back to the hub for the re-attempt); `LockedOn` keeps the first lock time | The documented one free re-attempt, without a separate status |
| 2026-09-28 | "One open group per customer + address" is enforced by the filtered unique index `UX_DeliveryGroup_Customer_Address_Open` | The database settles simultaneous orders; the code retries on the violation (2.2) |
| 2026-09-28 | No hard-coded business values: tenant settings (fees, join days, time zone, currency, SMS sender) have no defaults in C# or SQL; every tenant states its own | Owner rule. A silent default would give a new operator Dhaka's prices |
| 2026-09-28 | Deliver fast and Don't hold orders get their own group, created `Locked` with `LocksAt` = the next midnight (tenant zone) | They never wait, so they must not hold the customer's one `Open` slot; the lock job and trip planning read the same `LocksAt` |
| 2026-09-28 | Create Order locks an open group whose `LocksAt` has passed before opening the next one | Correct before the lock job (2.5) exists and whenever the job runs late; `LockedOn` records the deadline, not the late moment |
| 2026-09-28 | `Order.DeliveryGroupId` NOT NULL; the change and the backfill of older orders run in `Scripts/Pre` | SqlPackage refuses NULL → NOT NULL on a table with rows; DCN makes deliberate schema changes in the pre phase |
| 2026-09-28 | The merchant API never returns group data (number, delivery day, size) | A merchant must not learn that the customer also bought elsewhere |
| 2026-09-28 | Create Order returns only `fee`, what this order adds (base, extra or 0), stored as `Order.AddedFee`; no `groupFee` | The group total reveals how many other shops are in the delivery. Stored so a replay answers the same |
| 2026-09-28 | A Don't hold order costs the base fee; Deliver fast costs `FastDeliveryFee` | Don't hold is the merchant's choice, so the customer pays as for a one-shop group |
| 2026-09-28 | A new NOT NULL column with no default on a table with rows goes through `Scripts/Pre` (add nullable, fill, NOT NULL) | SqlPackage cannot add it, and a SQL default would be a hidden business value |
| 2026-09-28 | The checkout quote is read only and shares the group choice and fee query with Create Order; it returns `fee`, `currency` and `joinsDelivery`, never shops or a group total | The quote must equal the order's fee; a checkout may call it for any visitor, so it must leave no customer behind |
| 2026-09-28 | Hangfire owns its `HangFire` schema in the application database; the SQL project does not model it | Third-party tables upgraded by Hangfire itself; the publish never drops objects outside the project |
| 2026-09-28 | Tenant jobs: one recurring entry per job queues one run per active tenant, stored as (job name, tenant id); the runner sets the tenant on the job's scope | Tenants fail and retry independently; new tenants need no new schedule; Hangfire cannot load generic methods |
| 2026-09-28 | The lock job saves group by group and skips a group changed meanwhile | An order past the deadline may lock the same group; one conflict must not stop the rest |
| 2026-09-28 | Job dashboard `/jobs` for platform admins only | It lists every tenant's jobs |
| 2026-09-28 | Ship now locks the group and delivers it the next day (tenant midnight after the press); `LocksAt` moves earlier, never later | Keeping Day 3 would only stop other shops joining and give the customer nothing; next day matches Deliver fast and the lock job and trip planning keep reading `LocksAt` |
| 2026-09-28 | Customers need no sign-up: a customer is created automatically from the phone number (E.164, per tenant) on their first order, and the SMS-code login is an optional way into the same record | Owner's choice. Customers buy at the shop's checkout and may never visit OneDrop; grouping must work for them from the first order |
| 2026-09-28 | Domain events go to `Notifications.OutboxMessage` in the change's own transaction (save, add outbox rows with the new ids, save, commit); the rows hold ids only and the SMS is written when sent | A change is never saved without its message nor a message without its change; texts built at send time never repeat stale data after a retry |
| 2026-09-28 | The outbox sender runs every 5 seconds in process (`OutboxDispatcher`, per tenant through `TenantJobRunner`), not as a Hangfire recurring job | Hangfire's recurring jobs run at most once a minute; "your order joined" should arrive while the customer is still at checkout |
| 2026-09-28 | A failed message is retried after 1, 2, 4 and 8 minutes and marked `Failed` after 5 attempts | Rides out a short gateway outage without flooding it; a failed message stays for someone to look at |
| 2026-09-28 | The customer API (`/api/v1/deliveries`) uses the customer's sign-in cookie on the tenant subdomain, not an API key; cookie challenges on `/api` are 401/403, not redirects | Merchants' keys must never reach a delivery; the cookie is host-only, so the tenant comes from the subdomain as for the pages |
| 2026-09-28 | The frontend stays Razor Pages (PWA for the customer and rider screens, SignalR for live dashboards); no React or Angular in the MVP. React may be reconsidered for the rider app only, at tasks 3.4–3.5 | Owner's choice. Host-only cookies per tenant subdomain keep tenants apart; the screens are mostly forms and lists; a separate SPA would cost about a week of the remaining plan |
| 2026-09-28 | "You save" compares the group fee with each shop sent separately at the tenant's own base fee; nothing is shown for one shop | The documentation's comparison (৳60 per courier) is the one-shop price; a separate "courier price" setting would be a second number to keep in step |
| 2026-09-28 | An order is "for delivery" unless cancelled, refused or returned (`Order.IsForDelivery`); the fee, the COD due and the package count all use it | One rule, so the page, the door and the fee can never disagree about what is being delivered |
| 2026-09-28 | The customer app's service worker never caches pages, only the offline page and its style; the manifest is linked by a plain path, not `~/` | Deliveries change all day and a stale page would show the wrong fee or day; `~/` fingerprints the URL per build, and a manifest should keep one address |
| 2026-09-28 | `Network.PickupRoute`, one active route per zone, its time a tenant setting in local time; stops are worked out when the sheet is opened, not built by a job | An order placed just before the run is on the sheet; nothing to keep in step. `Route` would clash with ASP.NET's `[Route]` attribute |
| 2026-09-28 | A parcel is collected by the route of its order's pickup point's zone, not the merchant's `ZoneId` | That is where the parcel physically is; a merchant may have pickup points in several zones |
| 2026-09-28 | The label is `{order number}-{package}` (`PackageLabel`), printed as text and as a QR code holding only that code; it names the destination hub but never the delivery group | The scanner's tenant decides whose parcel it is; a label handed around must not tell a merchant about the customer's other shops |
| 2026-09-28 | Hub pages use policy `Operations` (hub staff or tenant admin, with a tenant claim) | The operator's own staff; merchants and customers are refused, another operator's route is a 404 |
| 2026-09-28 | Hub scans are per package (`Package.HubId`, `ReceivedOn`); the order is `AtHub` only when every package has reached a hub. Collecting at the shop is per order | Every parcel of a delivery must be on its shelf before the rider takes it; at the shop the collector takes the whole order |
| 2026-09-28 | A delivery gets the lowest free shelf number at its own hub at its first parcel scanned in there, and frees it when it leaves (dispatched, delivered, cancelled); shelf = `DeliveryGroup.Shelf`, labelled `MIR-01` | Shelves are fixed places with fixed numbers; reusing the lowest keeps the numbers small. The unique index settles two scans taking the same shelf |
| 2026-09-28 | Hub staff pick their hub on the page (`?hub=MIR`); users have no hub | The MVP has one hub-staff login per operator; a hub claim on the user can come with the rider app |
| 2026-09-28 | The hub shuttle is a scan at each end (load, then receive), not the cut "transfer" button; a package on the shuttle has no `HubId` and a `ShuttleToHubId` | "Scan at every handover"; the manifest shows what is still to load and what is on its way, and a package is never at two hubs |
| 2026-09-28 | No shuttle job and no shuttle time setting: the manifest is worked out when opened and staff load when the shuttle leaves | Same reasoning as the pickup sheet; a time would be a tenant setting with nothing yet reading it |
| 2026-09-28 | An order stays `AtHub` while a package is on the shuttle; "every package in" means every package has been received at a hub (`ReceivedOn`) | The order status says the parcels are in the hub network; where each one is is on the package |
| 2026-09-28 | A bike's limit is two numbers on the rider, `MaxParcels` and `MaxWeightGrams`, with no default | Bikes differ; parcels and weight are both what a bike cannot exceed, and both are already on every package |
| 2026-09-28 | Trips are planned every 15 minutes from the start of delivery day, not once at 08:00; planning is repeatable and changes nothing already planned | Parcels still arrive on delivery day (next-day orders are collected at 2 PM that day); tenants live in different time zones, so a fixed hour would be one tenant's hour |
| 2026-09-28 | A delivery is planned once at least one of its parcels is on its shelf; at Start trip only orders with every parcel here go out, and a delivery with nothing ready comes off the trip | "Merchant not ready → the group leaves without it"; a delivery must not wait all day for one late shop |
| 2026-09-28 | One trip per rider a day; the planner fills riders in name order, late deliveries first, then area by area | One evening run (5–9 PM); area order keeps a rider's stops together without maps or geocoding |
| 2026-09-28 | `TripStop.DeliveryDate` copies the trip's date so a unique index keeps a delivery on one trip a day; a failed attempt gets a new stop another day | The database settles two planners at once (job and button), as the shelf index does for scans |
| 2026-09-28 | Riders sign in as staff on their tenant's subdomain; the rider row points at the login (`Rider.UserId`), not the login at the rider | Identity stays unchanged; a rider can exist before they have a login |
| 2026-09-28 | **Market review:** OneDrop Dhaka's fast fee is ৳70 (DbUp 003); standard stays ৳60 + ৳25 | Dhaka couriers charge merchants ৳55–70 for delivery within 24 h of pickup, so a Facebook order usually reaches the door on Day 3 anyway: ৳60 for Day 3 is the market price. Fast is at the door the day after the order (same day from pickup), which couriers sell for about ৳105. At ৳60 fast cost the same as waiting, so nobody waited for other shops |
| 2026-09-28 | Fast and Don't hold deliveries can be joined until the new order's pickup route has left on the delivery day; a new order joins the delivery that leaves soonest (3.4a) | A delivery travelling alone was lost density; a shop ordering the next morning still makes the evening trip |
| 2026-09-28 | Ship now brings the day forward only as an upgrade: it adds the fast fee minus the base fee, and is free on Day 2 (3.4a) | A free Ship now would undo the fast price; on Day 2 it moves nothing |
| 2026-09-28 | The fee includes a weight allowance per shop, with a fee per kg above it (tenant settings, 3.4a) | Every Dhaka courier prices by weight (Pathao ৳60 up to 500 g, ৳90 at 2 kg, +৳15 per kg); a flat fee would carry a 6 kg parcel at the price of a lipstick |
| 2026-09-28 | Daily timetable: pickups 11:00–13:30 staggered by zone, shuttle about 14:30, riders out at 17:00 (3.4a) | With a 19:00 shuttle an order from another zone placed late on Day 2, or any fast order from another zone, missed its trip |
| 2026-09-28 | A shop pays nothing for a delivered order, but pays a return charge for a refused parcel and a late-handover fee for an order left behind (3.7) | 20–30% of COD parcels come back in Bangladesh; the customer pays OneDrop only at the door, so a refusal earned nothing. Couriers charge merchants for failed deliveries too (Paperfly: one delivery charge) |
| 2026-09-28 | An order left behind goes out the next day, or joins the customer's open delivery, at the extra-shop fee only (3.5) | The customer pays what was promised; the shop that was not ready pays for the second trip |
| 2026-09-28 | Advance payment by risk, not for every new customer: one-tap SMS confirmation for a new COD customer; the first shop's fee in advance after a refusal or no-show, or when the merchant asks; never for a product paid online (3.6, 3.8) | Every customer is new at launch. Paying the delivery charge in advance by bKash is already normal with Facebook sellers and cuts fake orders, so it is acceptable when it targets risk; a refusal at one shop protects the others |
| 2026-09-28 | Deliveries for the same phone in the same area on one trip are one stop and one fee (3.5); the customer is asked to combine deliveries whose addresses almost match, and the spelling is learnt (4.7) | A missed address match must never cost the customer ৳60 + ৳60 for one visit |
| 2026-09-29 | A delivery group has a kind (`Waiting`, `NextDay`, `ShippedNow`); only next-day kinds take orders after they lock | Keeps the must-pass "an order on Day 3 starts a new group" while fast and shipped-now deliveries fill up; the kind also says who pays the fast difference |
| 2026-09-29 | A new order joins the delivery that leaves soonest and can take it, also when it waits (a waiting order may go with a fast delivery tomorrow); a fast or Don't hold order joins the open group only when that arrives tomorrow | Density, and one door visit where two deliveries would arrive the same day; nothing ever arrives later than its speed promised |
| 2026-09-29 | Joining after the lock is decided by the order's pickup route: its next run must fall on or before the delivery day | The route is the only link between the shop and the hub; a run on the delivery day reaches the hub by about 14:00, before the 17:00 trips |
| 2026-09-29 | A fast delivery's first shop pays the fast fee, every other shop the extra-shop fee; Ship now that brings the day forward charges base + (fast − base), never less than base | One formula for every delivery; the fast price is not undone by joining or by Ship now |
| 2026-09-29 | Weight: 2 kg per shop in a delivery, then per started kg (Dhaka ৳15, Chattogram ৳20; owner); "You save" leaves weight out | Couriers charge per kg too (Pathao +৳15), so weight is no saving and no loss against them |
| 2026-09-29 | The new tenant settings are nullable in SQL; the application does not serve a tenant without them | The launch seed inserts tenants without them and must not be edited, so a NOT NULL column would break a fresh database; a skipped tenant is loud (log) and never gets another operator's value |
| 2026-09-29 | Pickup routes staggered 11:00–13:30, farthest zones first (owner's times, DbUp 005) | Every parcel at its hub by about 14:00, shuttle about 14:30, riders at 17:00 |
| 2026-09-29 | A rider's stop is a visit: every delivery on the trip for the same customer in the same area, one fee split over their stops | "One stop, one fee" from the market review; each stop keeps its own part for the ledger (3.7) |
| 2026-09-29 | The rider checks the amount with a plain link and hands over only by confirming exactly that amount | "No fee, no handover" without JavaScript, and a mistaken tap changes nothing; a stale page cannot hand over at the wrong price |
| 2026-09-29 | Nobody home: back to the hub (scanned in again) and out another day free; the second failed visit sends the orders back to their shops | The documented one free re-attempt; "scan at every handover" |
| 2026-09-29 | An order left behind moves to the customer's open delivery or a `FollowUp` delivery the next day, first shop at the extra-shop fee; `LeftBehindOn` is recorded and `AddedFee` never changes | The customer pays what was promised; the merchant's fee stays what they were told; 3.7 decides the late-handover fee from the record |
| 2026-09-29 | A refused order keeps `Refused` while its parcels come back through the hub, and becomes `ReturnedToMerchant` at a "Return to the shop" scan | The existing state machine, and a scan at every handover |
| 2026-09-29 | 3.6 is split: 3.6a door payment and receipt, 3.6b confirmation and advance payment (owner) | Two changes, each tested and reviewed on its own |
| 2026-09-29 | A wallet payment at the door is a gateway request for exactly the amount due, shown as a QR on the rider's phone; the rider hands over only once the gateway says it is paid. The MVP gateway is a fake paid by hand on `/Dev/Payments` (owner) | "No fee, no handover" without trusting a screenshot; a real bKash or Nagad gateway replaces the adapter |
| 2026-09-29 | One payment per visit, fee and COD kept apart; every stop of the visit points at it; a stop that collects money must have a paid payment | The ledger (3.7) splits COD per shop and the fee per delivery from the stops; the domain enforces "no fee, no handover" |
| 2026-09-29 | An unpaid QR is cancelled when the wallet, the amount or the outcome changes; one the customer has paid is never dropped, the rider must hand over what it paid for | No double charge, and no refunds to handle in the MVP |
| 2026-09-29 | For 3.6b: an order waiting for its advance is not collected until paid; 10 accepted deliveries and a customer never pays in advance (tenant setting) (owner) | No parcel travels to an unpaid door; the two-way cost is what advance payment exists to avoid |

## Quick reference

| | |
|---|---|
| Run | `./tools/db/publish.ps1` then `dotnet run --project src/Web` → http://dhaka.localhost:5080 |
| Databases | `OneDrop` (dev), `OneDrop-Test` (integration tests) on `ras-x2,1433` |
| Demo logins and API keys | [README](../README.md#demo-logins-development-only-password-onedrop2026) |
| Database rules | [Documentation/Database.md](../Documentation/Database.md) |
| Code conventions | [Documentation/Conventions.md](../Documentation/Conventions.md) |

---

## Daily log

Newest first. One entry per working day: what was done, how it was tested, what is next.

### 2026-09-29
- **Decided with the owner:** weight allowance 2 kg per shop, then ৳15 per started kg in Dhaka and ৳20 in Chattogram;
  the staggered pickup times proposed (Dhaka 11:00–13:30, Chattogram 11:00–13:00, farthest first).
- **Done (task 3.4a):** `Domain/Grouping/DeliveryGroupKind`, `DeliveryGroup.Kind`, `CanTake`, `ShipNowBringsForward`;
  `DeliveryFeeCalculator` (weight per shop, fast first shop, `shippedNow`, `ShipNowFee`), `FeeSchedule` and `FeeLine`
  with weights; `Tenant.WeightAllowanceGrams` / `ExtraKgFee` (SQL nullable, `TenantCatalog` skips a tenant without
  them); SQL `Grouping.DeliveryGroup.Kind` + `chk_DeliveryGroup_Kind`, `Platform.Tenant` columns and checks;
  `Pre/003_DeliveryGroupKind`, DbUp `2026/004_WeightAllowance`, `2026/005_StaggeredPickupTimes`; `DeliveryGrouping`
  (soonest delivery that can take the order, pickup route lookup, shared with the quote); quote `pickupPointId` and
  `weightGrams`; Ship now `addedFee` and the "Deliver tomorrow for +৳10" button; rider stops and "My deliveries"
  priced with weights and kind; shuttle manifest ordered by delivery day; "Next day" marks by the delivery's kind on
  hub pages; README.
- **Tested:** build 0 errors, no new warnings; 25 new domain tests (kinds; a next-day delivery takes an order whose
  pickup falls by its delivery day, not after, and none without a route or once out; fast orders join the open group
  only on Day 2; a locked waiting group takes nothing on Day 3; Ship now on Day 1 becomes `ShippedNow` and stays
  joinable, on Day 2 moves nothing; each started kg at both tenants' prices, per shop across its orders, an order
  taking its shop over the allowance, refused parcels not weighed, weight not in the saving; Ship now fee and never
  cheaper; shops joining a fast delivery) and new integration tests (fake clock around the shops' real route times:
  a second shop joins the fast delivery and the quote says +৳25 half an hour before its route leaves on the delivery
  day, a third shop after its route opens a new group at ৳60; a fast order after the last run opens its own; a 3 kg
  order at Dhaka ৳75 and Chattogram ৳90, the quote with `weightGrams` equal; "My deliveries" offers Ship now at +৳10
  and the page shows the button, the message and ৳95). Five existing tests changed with the rules (a Don't hold order
  now joins the fast delivery; the shipped delivery takes the next shop at +৳25; Ship now's ৳10; route times).
  Mutation: dropping the pickup condition fails 2 domain and both new integration tests. 195 + 6 + 90 = 291 pass,
  none skipped. `Pre/003` dry-run in a rolled-back transaction on dev (8 groups `NextDay`, 20 `Waiting`), then both
  databases published: `Grouping.DeliveryGroup` and `Platform.Tenant` rebuilt with their rows, three scripts ran.
  Live on dev: phone 01816420937 in Mirpur 10 — Fashion House fast quote and OD-100054 ৳70 (DG-100031, `NextDay`),
  Gadget BD quoted +৳25 and OD-100055 ৳25 in DG-100031, Beauty Shop 3 kg quoted and charged ৳40 (+৳25 + ৳15) in
  DG-100031; texts "arriving on Wed 30 Sep"; Chattogram 3 kg OD-100057 ৳90; weight 0 → 400; another shop's key and
  Chattogram's key 404 on OD-100055. Phone 01816420939: Fashion House OD-100058 ৳60 + Beauty Shop OD-100059 ৳25 open
  in DG-100033; signed in by SMS code, "My deliveries" showed ৳85 and "Deliver tomorrow for +৳10 … instead of
  Thursday"; pressed → "closed … Wednesday 30 September. Its fee went up by ৳10", fee ৳95; then Gadget BD OD-100060
  joined at ৳25 → ৳120, "You save ৳60". Hub "Pickup routes" shows the new times. No errors in the app log.
- **Next:** task 3.5, delivery screen and attempts.
- **Committed:** task 3.4a as `31a994f` on branch `day3` (created from `day2` at `d336812`), pushed to `origin/day3`.
- **Done (task 3.5):** `Domain/Delivery/StopOutcome`, `TripStop.Complete` (outcome, fee and COD collected),
  `Trip.Finish`; `DeliveryGroupKind.FollowUp` and `DeliveryGroup.FollowUp`; `Order.FollowUpIn`, `LeftBehindOn`,
  `ReturnToMerchant`, refused orders received at a hub; `DeliveryFeeCalculator` takes the delivery's kind (FollowUp
  first shop = extra-shop fee) and `VisitFees`; `FeeLine.Of`; SQL `Delivery.TripStop` (+ `Outcome`, `FeeCollected`,
  `CodCollected`, `CompletedOn`, checks), `Orders.Order.LeftBehindOn`, `chk_DeliveryGroup_Kind` 1–4;
  `Application/Delivery/TripVisits`, `Door/DoorHandler`; `RiderDayHandler` by visits, follow-ups at Start trip;
  `HubScanHandler.ReturnAsync`; "My deliveries" shows the collected fee; rider page door actions; scan tab **Return to
  the shop** and the "Back to the shop" answer.
- **Tested:** build 0 errors, no new warnings; 12 new domain tests (a visit's fee: one delivery its own fee, two
  deliveries ৳60 + ৳25, a shop in both paid once, follow-up at the extra-shop fee and the dearest kind first, a
  delivery with everything refused adds nothing; follow-up delivery locked for tomorrow and joinable; an order left
  behind keeps its `AddedFee`, records `LeftBehindOn` and raises one event, only a waiting order of the same customer;
  refused parcel back at the hub then returned once, only a refused order; a stop done once and collecting only when
  something was handed over; a trip finishes only when out) and 3 new integration tests on hubs of their own (one
  door, one fee for a waiting and a fast delivery of the same phone with the address spelt two ways: ৳95, with Gadget
  BD refused ৳60 + COD, a wrong amount refused and nothing changed, stops ৳60/৳1,500 and ৳0, trip finished, the
  customer's page shows ৳60, the refused parcel back at the hub and returned, another operator has no trip; nobody
  home twice: back at the hub, not re-planned the same day, shelved again, then after a day the second failure sends
  it to the shop; the rider page: review "Collect ৳1,260", hand over by form, "Every stop is done"); the Start trip
  test now expects the unready order in a `FollowUp` on the freed shelf. Mutation: accepting any amount fails the door
  test. 207 + 6 + 93 = 306 pass, none skipped. Both databases published: `Delivery.TripStop` and `Orders.Order`
  rebuilt with their rows, new checks. Live on dev (Mirpur hub, Rafiq Hasan's trip): Shirin Akhter 01819274051 with
  Fashion House OD-100061 (COD ৳1,200) and Beauty Shop OD-100062 in DG-100034, Gadget BD fast OD-100063 (COD ৳800,
  "Flat 2A, House 8, Road 3") in DG-100035; Mahbub Alam 01819274052 OD-100064 in DG-100036; made due, scanned in
  except OD-100062, planned from the hub page. Before start Shirin was one stop, DG-100034 + DG-100035, ৳120; Start
  trip: "3 deliveries and 3 orders … 1 delivery stays at the hub (DG-100029) … 1 order … follow", then ৳95. At the
  door, Gadget BD refused: "Collect ৳1,260"; ৳1,000 refused ("The amount to collect is 1,260"); ৳1,260 → "Handed
  over". Mahbub nobody home → "We try again on another day, free"; trip Finished, collected ৳1,260. OD-100062 moved
  to follow-up DG-100037 (kind 4, tomorrow, `AddedFee` still ৳25), texted "arriving on Wed 30 Sep". Hub: OD-100063-1
  "Back to the shop Gadget BD", Return → "Returned", again → already scanned, a delivered parcel refused;
  OD-100064-1 back on MIR-03. The Dhaka rider's cookie on Chattogram → login, hub staff on `/Rider` → access denied.
  No errors in the app log. Not done: a phone-width screenshot of the door screen. The message "1 order was not
  ready and follow…" was corrected to "follows" afterwards (build checked into a scratch folder while the app was
  running from Visual Studio).
- **Next:** task 3.6, door payment.
- **Decided with the owner:** 3.6 is split into 3.6a (door payment and receipt) and 3.6b (confirmation and advance
  payment); a wallet payment is a gateway request the rider checks (fake gateway paid on `/Dev/Payments`); an order
  waiting for its advance is not collected until paid; a customer with 10 accepted deliveries never pays in advance.
- **Done (task 3.6a):** `Domain/Payments` (`Payment`, `DoorPayment`, `PaymentReceived`, method/status/purpose and
  `DisplayName`); `TripStop.PaymentId` and `Payment`, `Complete` takes the payment; SQL schema `Payments`, table
  `Payment` with its checks and two indexes, `Delivery.TripStop.PaymentId` + `FK_TripStop_Payment` + `IX_TripStop_Payment`;
  EF mapping (`PaymentsConfiguration`, `Schemas.Payments`, `IAppDbContext.Payments`); `IPaymentGateway`,
  `FakePaymentGateway` + `FakePaymentLog`; `DoorHandler` (`RequestQrAsync`, `HandOverAsync` with the method, unpaid QRs
  dropped, nobody home drops them too); outbox `PaymentReceivedMessage` and the receipt in `CustomerTexts`; rider day
  `Cash` and `PaidBy`; rider page wallet buttons, QR (`LabelQrCode.Svg(string)`), check and hand over; `/Dev/Payments`
  and its layout link; styles.
- **Tested:** build 0 errors, no new warnings; 4 new domain tests (cash paid at once with one receipt event; a wallet
  payment pending, requested once, paid once, never cancelled after; a cancelled QR cannot be paid, cash has no
  gateway, no zero or negative amounts; a stop with money needs a paid payment, a not-home stop none, a second delivery
  of a paid visit links with ৳0) and 3 new integration tests on hubs of their own (bKash: the same QR on a second ask
  and on the stop, "not arrived yet" and "no QR for Nagad" with nothing handed over, paid in the fake gateway, a
  refusal then refused as already paid, handed over, one `Paid` payment linked from the stop, rider's cash ৳0, and the
  receipt text; switching bKash → Nagad cancels the first, cash cancels the second, everything refused after a QR
  cancels it and pays nothing, cash counted for the hub, QR for cash / unknown method / wrong amount refused; the page:
  Nagad QR shown as SVG, check before paying stays at the door with the reason, then hands over). Existing door tests
  pass the method; the page test checks "Cash to hand in". Mutation: handing over without asking the gateway fails 2
  tests. 211 + 6 + 96 = 313 pass, none skipped. The publish script was reviewed first: the new schema and table,
  `Delivery.TripStop` rebuilt with its rows copied (plus the usual check re-creates); both databases published. Live on
  dev (app restarted): Rumana Haque 01819274061 with Fashion House OD-100065 (COD ৳1,200) and Gadget BD OD-100066
  (COD ৳800) in DG-100038, Tanvir Ahmed 01819274062 with Beauty Shop OD-100067 (COD ৳650) in DG-100039; scanned in
  (MIR-04, MIR-05), made due by hand (`OpenedOn` moved back too: `chk_DeliveryGroup_LocksAt`), planned from the hub
  page onto Sumon Ali's trip, started. DG-100038 "Collect ৳2,085"; bKash QR shown; check before paying → "The bKash
  payment of 2,085 has not arrived yet"; paid on `/Dev/Payments`; check → "Handed over. Collected ৳2,085 by bKash".
  DG-100039 "Collect ৳710"; ৳500 refused back to the door; cash ৳710 → handed over; "Collected ৳2,795", "Cash to hand
  in ৳710", "Every stop is done". Receipts on `/Dev/Sms`: "OneDrop receipt: ৳2,085 paid by bKash on Tue 29 Sep for
  delivery DG-100038. Delivery fee ৳85. Fashion House OD-100065 ৳1,200, Gadget BD OD-100066 ৳800. Thank you." and the
  cash one for ৳710. Database: two `Paid` payments (Fee/Cod 85/2,000 and 60/650, rider 2, tenant 1), each stop linked,
  two receipt rows sent. The Dhaka rider's cookie on Chattogram 403; Chattogram's rider has no such stop. No errors in
  the app log. Not done: a phone-width screenshot of the QR card.
- **Next:** task 3.6b, confirmation and advance payment.

### 2026-09-28
- **Done:** connection strings moved out of the repository into git-ignored `appsettings.Local.json` /
  `testsettings.Local.json`; private repository https://github.com/talha33177net-eng/OneDrop created and `main`
  pushed (single commit, owner as the only author and contributor); conventions moved to `Documentation/`;
  `Documentation/Project-Context.md` written as the handover document for any new session.
- **Tested:** build clean; 43 domain + 6 architecture + 13 integration tests pass; integration run added orders
  only to `OneDrop-Test` (dev `OneDrop` unchanged at 4); `publish.ps1` and the app work from the local files;
  pushed tree scanned for secrets and local files (none); every relative link in the docs resolves.
- **Done (task 2.1):** `Domain/Grouping/DeliveryGroup` + `DeliveryGroupStatus`; SQL project: schema `Grouping`,
  sequence `DeliveryGroupNumber` (`DG-100001`), table `DeliveryGroup` with `UX_DeliveryGroup_Customer_Address_Open`
  (`WHERE Status = 1`) and `IX_DeliveryGroup_Tenant_Status_LocksAt` for the lock job; `Orders.Order.DeliveryGroupId`
  (nullable, FK, index); EF mapping and `IAppDbContext.DeliveryGroups`.
- **Tested:** build 0 errors, no new warnings; 18 new domain tests (lock time in the Dhaka zone around UTC
  midnight, Day 1/Day 2 join and Day 3 does not, Ship now, state machine, re-attempt keeps `LockedOn`) and 3 new
  integration tests (number and tenant stamped, a second open group for the same customer + address is refused
  by the index, a new one opens once the first is locked); 61 + 6 + 16 = 83 pass, none skipped, schema-match
  green. Publish script reviewed first: the new column rebuilds `Orders.Order` with its rows copied; both
  databases published. Live: dev `OneDrop` still has its 4 orders and packages after the rebuild; the running app
  returned OD-100001/OD-100002 to their merchants and 404 for another merchant's and another tenant's order.
- **Done (task 2.2):** `DeliveryGrouping` service in Create Order (join / open / race recovery / lock a group
  past its deadline); `DeliveryGroup.OpenAlone` and `LockIfDue`; `Order.WaitsForGroup`, `Order.PlaceIn`;
  `Order.DeliveryGroupId` NOT NULL with `Pre/001_GroupExistingOrders` (backfill + ALTER, guarded); tenant setting
  defaults removed from `Tenant` (C#) and `Platform.Tenant` (SQL), unused `Tenant(name, slug)` constructor removed.
- **Tested:** build 0 errors, no new warnings; 9 new domain tests (next-day solo group, lock at the deadline and
  not before, which orders wait, placing in another address's group is refused) and 10 new integration tests
  (3 shops → 1 group, home and office → 2, 9 parallel orders → 1, fast and Don't hold alone beside the open
  group, response has no group data, Day 1 / Day 2 join and Day 3 opens a new group with the old one locked at
  its deadline (fake clock), join days from the tenant, and the race forced with an interceptor — shown to fail
  with the recovery disabled). 70 + 6 + 26 = 102 pass, none skipped. Backfill dry-run in a rolled-back
  transaction on both databases, then published: dev OD-100001/002/004 share one group, OD-100003 has its own.
  Live: 9 parallel orders from the 3 Dhaka shops for a new phone → 201s, one open group locking at Wednesday
  00:00 Dhaka; a fast order → its own locked next-day group; the same phone in Chattogram → a separate group.
- **Committed:** tasks 2.1–2.2 as `bb24f7b` and task 2.3 on branch `day2` (not pushed).
- **Done (task 2.3):** `Domain/Pricing/DeliveryFeeCalculator` + `FeeSchedule` (`TenantInfo.Fees`); `Order.AddedFee`
  set by `DeliveryGrouping` when it places the order (the other merchants' orders in the group are read with the
  merchant filter lifted, for the calculation only); `fee` in the Create Order and Get Order responses, no group
  total; `Orders.Order.AddedFee DECIMAL(10,2) NOT NULL` with `Pre/002_PriceExistingOrders` (add nullable, price
  each older order in arrival order, NOT NULL); query filter names moved to `Application.Abstractions.QueryFilters`.
- **Tested:** build 0 errors, no new warnings; 17 new domain tests (1–4 shops ৳60/85/110/135, Chattogram
  prices, same shop once, cancelled/refused/returned shops not charged, fast fee, added fee per order, Don't
  hold = base, no negative fee) and 5 new integration tests (3 shops → 60 + 25 + 25 and a ৳110 group, same shop
  adds 0, fast and Don't hold fees, Chattogram's own prices, replay and GET show the same fee), amounts read from
  the tenant. 87 + 6 + 31 = 124 pass, none skipped, schema-match green. `Pre/002` dry-run in a rolled-back
  transaction on dev (DG-100003 = 60 + 25 + 25), then both databases published; dev kept its 16 orders. Live:
  a new phone, Fashion House ৳60, Gadget BD ৳25 (replay: same order, ৳25), Beauty Shop ৳25, Fashion House again
  ৳0 → one open group of 3 shops totalling ৳110; fast ৳60 and Don't hold ৳60 alone; Chattogram ৳70; GET shows the
  fee to its merchant and 404 to another merchant and another tenant; no group data in any response.
- **Done (task 2.4):** `GET /api/v1/quote` (`Application/Pricing/GetQuote`, `QuoteController`);
  `DeliveryGrouping.QuoteAsync` (read-only twin of the group choice in `SaveInGroupAsync`, same fee query).
- **Tested:** build 0 errors, no new warnings; 11 new integration tests (for 3 shops each quote equals the fee
  its order then gets, 60 / +25 / +25; a shop already in the delivery is quoted 0 with another spelling of phone
  and address; fast, Don't hold and another address quote a delivery of their own; Chattogram quotes its own
  price beside a Dhaka delivery; a quote creates no customer; bad phone, missing area or line1 → 400 naming the
  field; another tenant's area → `quote.area.unknown`; no key → 401; fake clock: the last second of Day 2 quotes
  +extra, Day 3 quotes a new delivery and leaves the open group unlocked). No domain rule added (the calculator
  is already covered). 87 + 6 + 42 = 135 pass, none skipped. No schema change, no publish. Live on dev with a new
  phone: quote ৳60 → OD-100024 ৳60, quote +৳25 → OD-100025 ৳25, quote +৳25 → OD-100026 ৳25, Fashion House
  again ৳0, fast ৳60, Don't hold ৳60, office ৳60, Chattogram ৳70, bad phone 400, no key 401.
- **Done (task 2.5):** Hangfire 1.8.25 (`Infrastructure/Jobs`: `JobsSetup`, `TenantJobRunner`, `TenantJobRegistry`),
  `ITenantJob`, `LockDueGroupsJob`, recurring `lock-due-groups` every 5 minutes, dashboard `/jobs`, `Jobs:Server`
  setting (off in the test host).
- **Tested:** build 0 errors, no new warnings; 9 new integration tests (only groups past their deadline lock, with
  `LockedOn` = the deadline even when the job runs late; a Dhaka run leaves Chattogram's due group open until
  Chattogram's run; an order after the lock opens a new group; a group locked by someone else mid-run is skipped
  and the rest still lock — shown to fail with the conflict handling disabled; the runner sets the tenant from
  its parameter, skips an inactive tenant and refuses an unknown job; the fan-out queues one job per active
  tenant; the scheduled job and the jobs it queues survive Hangfire's storage format; the test host runs no job
  server). Fake clock in January 2026 so a run never locks another test class's open group. 87 + 6 + 51 = 144
  pass, none skipped. No SQL project change; Hangfire installed its tables on first start. Live (schedule
  overridden to every minute): the **first** live run failed — Hangfire could not load the generic job method the
  unit-level tests had called directly — fixed by storing jobs by name, and a round-trip test added (checked to
  fail on a generic method). Second run: DG-100012 (deadline moved to the past by hand) locked with `LockedOn` =
  its deadline, "dhaka Locked 1 of 1", "chattogram Locked 0 of 0", every other group untouched; the quote for that
  customer then said ৳60, new delivery, and OD-100027 opened DG-100013. `/jobs`: platform admin 200 (lists
  `lock-due-groups`), anonymous → login, Dhaka tenant admin → access denied.
- **Committed:** tasks 2.4–2.5 on `day2` (not pushed).
- **Next:** task 2.6, Ship now.
- **Done (task 2.6):** `DeliveryGroup.ShipNow` and `NotOpenForShipNow`; `Application/Grouping/ShipNow/ShipNowHandler`;
  `Web/Api/V1/DeliveriesController` (`POST /api/v1/deliveries/{number}/ship-now`); "My deliveries" lists open
  deliveries with a Ship now button; the sign-in cookie answers `/api` with 401/403.
- **Tested:** build 0 errors, no new warnings; 6 new domain tests (Day 1 → next day, including 23:59 and the UTC
  date change; Day 2 → the same Wednesday; past the deadline → locked as due; locked, cancelled and travel-alone
  groups refused) and 5 new integration tests with a real SMS-code sign-in (the owner ships now → 200 with
  tomorrow's date, the group locked with an earlier `LocksAt`, a second press 409, the next shop's order opens a
  new delivery at the base fee; another customer → 404 from the API and "not found" from the page, group still
  open; the same phone signed in on Chattogram → 404; the page lists the delivery with its shops and the button
  closes it; anonymous and a merchant API key → 401). The ownership test was shown to fail with the customer check
  removed. 93 + 6 + 56 = 155 pass, none skipped. No schema change, no publish. Live on dev, new phone 01736878920:
  Fashion House + Gadget BD → DG-100014 (delivery Wed 30 Sep) listed on the page; another signed-in customer 404,
  anonymous 401, merchant key 401, the owner's cookie on the Chattogram host 401; the owner's Ship now → 200
  `2026-09-29`, `LocksAt` Tue 00:00 Dhaka, a second press 409; Beauty Shop then ৳60 in a new DG-100015; the page
  button closed DG-100015 ("We deliver it on Tuesday 29 September"); a page POST without the anti-forgery token
  400. No errors in the app log.
- **Next:** task 2.7, outbox and domain events.
- **Committed:** task 2.6 as `3155a71` on `day2` (not pushed).
- **Done (task 2.7):** `IDomainEvent` on `Entity`; `OrderPlacedInDelivery`, `DeliveryGroupLocked`;
  `Domain/Notifications/OutboxMessage` (retry rule); SQL schema `Notifications` and table `OutboxMessage` with
  `IX_OutboxMessage_Tenant_Status_NextAttemptOn`; `AppDbContext.SaveChangesAsync` writes the outbox in the change's
  transaction; `Application/Notifications` (`OutboxContracts`, `SendOutbox/CustomerTexts`, `SendOutboxJob`);
  `Infrastructure/Jobs/OutboxDispatcher` every `Jobs:OutboxInterval` (5 s); the MARS "savepoints disabled" warning
  is ignored (the outbox transaction is rolled back whole).
- **Tested:** build 0 errors, no new warnings; 8 new domain tests (placing twice raises one event; a refused
  placement raises none; lock at the deadline and by Ship now raise one event each, never before or twice, a
  travel-alone group none; outbox: pending at once, sent, waits 1/2/4/8 minutes then `Failed`, long error cut,
  type and payload required) and 5 new integration tests (an order is saved with its message; a lock that loses
  the race leaves exactly one message and keeps its events; the sender texts the joined, joined and travel-alone
  orders with the right days from the tenant's sender name and marks them sent; Ship now texts the shops and the
  new day; a failing gateway is retried after 1, 2, 4 and 8 minutes of a fake clock, not before, then `Failed`),
  plus the test host runs no outbox dispatcher. A mutation (placing twice without withdrawing the first event) was
  caught. 101 + 6 + 61 = 168 pass, none skipped. Publish: the new schema and table only, both databases. Live on
  dev, phone 01764090796: OD-100031 Fashion House and OD-100032 Gadget BD ("is in OneDrop delivery DG-100016 …
  until the end of Tue 29 Sep; we deliver on Wed 30 Sep") and OD-100033 fast ("DG-100017, arriving on Tue 29 Sep")
  texted about a second after each order; Ship now on DG-100016 texted "is closed. Your orders from Fashion House,
  Gadget BD arrive together on Tue 29 Sep"; OD-100034 in Chattogram saved under tenant 2 and texted from
  `OneDropCTG`; all five outbox rows `Sent` after one attempt; no errors in the app log.
- **Next:** task 2.8, customer group page (PWA).
- **Committed:** task 2.7 as `45c2a97` on `day2` (not pushed).
- **Done (task 2.8):** `Order.IsForDelivery`; `DeliveryFeeCalculator.Savings`;
  `Application/Grouping/CustomerDeliveries/CustomerDeliveriesHandler`; "My deliveries" rebuilt as delivery cards for
  a phone; layout `Head` section and favicon; `Shared/_CustomerApp` (manifest, touch icon, service worker) on the
  customer page and the customer sign-in page; `wwwroot/manifest.webmanifest`, `sw.js`, `offline.html`, `icons/`.
- **Tested:** build 0 errors, no new warnings; 14 new domain tests (savings for 2–4 shops, Chattogram's prices, one
  shop or a fast order saves nothing even with a fast fee below the base fee, a refused shop saves nothing; which
  statuses are for delivery) and 6 new integration tests through the query (three shops: one delivery, shops,
  4 packages 0 collected, fee and saving from the tenant, COD, days; a picked-up and a cancelled order change the
  collected count, the fee, the saving and the COD, the cancelled order stays listed; a fast order is its own locked
  delivery arriving tomorrow, listed first, fast fee, no saving; out with a rider stays on the way, delivered moves to
  earlier; another customer, and the same phone at Chattogram, are not listed, Chattogram at its own prices; the
  manifest, its icons, the service worker and the offline page are served). The signed-in page test now checks the
  shops, "0 of 2", the fee, the total at the door, the saving and the manifest link. The first run caught a
  constructor projection EF cannot filter and a `~/` manifest link rewritten to a fingerprinted URL; both fixed.
  115 + 6 + 67 = 188 pass, none skipped. No schema change, no publish. Live on dev, phone 01845127390: Fashion
  House (2 packages, COD 1500), Gadget BD (COD 2400), Beauty Shop (COD 650) → DG-100019; signed in by SMS code,
  headless Chrome at 390 px: no horizontal scroll, "Arriving Wednesday 30 September", join until Tuesday, 0 of 4
  collected, fee ৳110, COD ৳4,550, ৳4,660 at the door, "You save ৳70 against 3 separate deliveries", +৳25 for
  another shop; the manifest parses with no errors and the service worker registers for the subdomain. Anonymous
  and the same cookie on the Chattogram host → login; Ship now from the page → "Closed, arriving Tuesday 29
  September", no button. No errors in the app log.
- **Next:** task 2.9, Week 2 demo run.
- **Done (task 2.9):** Week 2 demo run on dev; Week 2 complete. No code change.
- **Tested:** build 0 errors, 0 warnings; 115 + 6 + 67 = 188 pass, none skipped. Live (app started with the lock
  job every minute), new phone 01912734580, one address in Mirpur 10: Fashion House quoted ৳60 and OD-100038
  charged ৳60; Gadget BD quoted +৳25 (phone spelt `+880 1912 734580`) and OD-100039 charged ৳25; Beauty Shop
  quoted +৳25 and OD-100040 charged ৳25; all three in one open group DG-100020 locking at Wed 00:00 Dhaka, three
  "joined" texts sent. The deadline was then moved into the past by hand; the job logged "dhaka Locked 1 of 1"
  (Chattogram 0 of 0), `LockedOn` = the deadline, and the "closed" text listed the three shops. Signed in by SMS
  code, "My deliveries" showed DG-100020 with the three shops, 0 of 3 collected, fee ৳110, COD ৳4,450, ৳4,560 at
  the door and "You save ৳70 against 3 separate deliveries" (its day reads Mon 28 Sep because of the moved
  deadline). Gadget BD's GET shows its order and ৳25 only, no group data; Fashion House's and Chattogram's keys get
  404; the same phone is quoted ৳70 at Chattogram; a Dhaka quote after the lock is ৳60, a new delivery; the customer
  cookie on the Chattogram host goes to the login page. No errors in the app log.
- **Next:** Week 3, task 3.1 pickup routes and QR labels.
- **Done (task 3.1):** `Domain/Network/PickupRoute` (`NextPickup`), `Domain/Orders/PackageLabel`; SQL
  `Network/Tables/PickupRoute.sql` and DbUp `2026/002_SeedPickupRoutes.sql`; EF mapping; `PickupRoutesHandler`,
  `PackageLabelsHandler`; `Web/Labels/LabelQrCode` (QRCoder 1.8.0); pages `/Hub/Routes`, `/Hub/RouteSheet/{id}`,
  `/Merchant/Labels`; policy `Operations`; print styles; "My orders" links to the labels.
- **Tested:** build 0 errors, no new warnings; 18 new domain tests (next run today until it has left, then
  tomorrow, around UTC midnight and across a summer-time change; labels write and read back, typed in lower case
  with spaces, every sequence up to 20, and junk, a delivery number or a package 0 or 21 refused) and 4 new
  integration tests (a route lists only its zone's pickup points with waiting parcels: a new Uttara shop's fast
  order with 2 labels is on the Uttara sheet, flagged, while a picked-up shop and the Mirpur shops are not, and
  the list counts what the sheet shows; each operator lists its own zones' 2 PM routes and another operator's
  route is null; hub staff and a tenant admin open the sheet, Chattogram hub staff 404, a merchant is denied,
  anonymous goes to login; a merchant prints 2 QR labels for its own 2-package order with the hub code and COD and
  no `DG-`, another shop's order prints nothing). `SchemaMatchesModelTests` now compares `time` precision. The
  first runs caught `৳` HTML-encoded inside a C# string and the 100-order cap cutting off the newest waiting
  orders (now newest first, with a notice); both fixed. 133 + 6 + 71 = 210 pass, none skipped. Published both
  databases: the new table and index only (plus sqlpackage's usual re-create of `chk_Tenant_GroupJoinDays`), one
  DbUp script. The link to ras-x2 was flaky: the first dev publish stalled before connecting and was stopped
  (nothing applied), one integration run hit a SQL login timeout and one could not reach the server; the
  following runs were clean. Live on dev, phone 01957461664: Fashion House OD-100041 (2 packages, COD ৳1,500) and
  Gadget BD OD-100042 (fast). Hub staff "Pickup routes": 7 Dhaka zones, next run Tue 29 Sep 2:00 PM (it was past
  2 PM), Mirpur 3 stops / 38 orders / 40 packages; the Mirpur sheet lists OD-100041-1 and -2 under Fashion House
  and OD-100042 "Next day" under Gadget BD. Tenant admin 200; Chattogram hub staff 404 on the Dhaka sheet and sees
  its own 5 zones; merchant → access denied; anonymous → login. Fashion House's labels for OD-100041: 2 QR labels
  (MIR, package 1 of 2, Nadia Rahman, Mirpur 10, COD ৳1,500), no group data; Gadget's OD-100042 prints nothing for
  Fashion House; "waiting" starts with OD-100041-1. Screenshots of the labels and the sheet looked right. No errors
  in the app log. Not done: scanning a printed QR with a phone.
- **Next:** task 3.2, hub scan-in and shelves (scan with `PackageLabel.TryParse`; also mark parcels `PickedUp`).
- **Done (task 3.2):** `Order.Collect`, `Order.ReceiveAtHub`, `ScanOutcome`; `Package.HubId` / `ReceivedOn`;
  `DeliveryGroup.Shelf`, `NeedsShelf`, `PutOnShelf`, `ShelfCode` (shelf freed on dispatch, delivery, cancel); SQL
  `Orders.Package` (+ `FK_Package_Hub`, `IX_Package_Hub`) and `Grouping.DeliveryGroup` (+ `chk_DeliveryGroup_Shelf`,
  `UX_DeliveryGroup_Hub_Shelf`); EF mapping; `Application/Network/HubScan/HubScanHandler` (hubs, collect, receive,
  shelves); pages `/Hub/Scan` and `/Hub/Shelves`, nav links, styles.
- **Tested:** build 0 errors, no new warnings; 17 new domain tests (collect once, a parcel at the hub counts as
  collected, cancelled and dispatched refused; `AtHub` only when every package is in, straight from `Created`, a
  second scan changes nothing, a parcel moved on to another hub; unknown package, cancelled order; a shelf is kept,
  freed on dispatch and taken again for a re-attempt, none for a cancelled group; shelf labels) and 7 new
  integration tests (three shops' parcels in Uttara share one shelf, a neighbour gets another, statuses `AtHub`, the
  shelf list counts 4 of 4; a Gulshan parcel at the Uttara hub is sent on to GUL with no shelf, then shelved at GUL;
  a dispatched delivery frees its shelf for the next; 4 deliveries scanned in parallel get 4 shelves and two
  packages of one order scanned at once both count — shown to fail with the retry switched off; collecting takes
  the order off the route sheet and a cancelled order is refused; Chattogram cannot find a Dhaka label or use a
  Dhaka hub, junk and a package 3 of 2 are refused; the pages scan and list shelves for hub staff, 404 for
  Chattogram hub staff, access denied for a merchant). The first run caught `Forget` detaching entities from the
  collection it was looping over (the retry path); fixed. 150 + 6 + 78 = 234 pass, none skipped. The publish script
  was reviewed first: `Orders.Package` and `Grouping.DeliveryGroup` rebuilt with their rows copied (plus the usual
  `chk_Tenant_GroupJoinDays` re-create); both databases published. Live on dev, phone 01834561290: Fashion House
  OD-100043 (2 packages), Gadget BD OD-100044, Beauty Shop OD-100045 in DG-100023. Hub staff on
  `/Hub/Scan?hub=MIR`: Collect OD-100043-1 → collected, -2 → "already scanned as collected"; Receive the 4 parcels
  (one typed in lower case) → all "Shelf MIR-01", "1 of 2 packages in" then "2 of 2"; again → "already scanned in
  here"; `DG-100020` → "is not a parcel label". OD-100046 to Gulshan 1 at MIR → "Send on to GUL hub", at GUL →
  "GUL-01". Shelves MIR: MIR-01 DG-100023 Wed 30 Sep, 3 orders, 4 of 4, Open. The Mirpur route sheet no longer
  lists them. Chattogram hub staff: 404 on the MIR pages, a Dhaka label at AGR "No parcel … is expected here";
  merchant → access denied; anonymous → login. Database: every package with its hub and time, history
  "Collected from the shop" / "Reached a hub without a pickup scan" / "Every package scanned in at a hub" by
  hub@dhaka. Screenshots at 390 px: no sideways scroll, the shelf table scrolls in its card. No errors in the app
  log. Not done: the camera scan on a real phone.
- **Next:** task 3.3, hub shuttle (list the parcels to send on per hub; receive at the destination with the same scan).
- **Done (task 3.3):** `Order.LoadForShuttle`, `Package.ShuttleToHubId` (cleared when received); "every package in"
  reads `ReceivedOn`; SQL `Orders.Package.ShuttleToHubId` with `FK_Package_Hub_ShuttleToHubId` and
  `IX_Package_ShuttleToHub` (the first hub key renamed `FK_Package_Hub_HubId`, as the conventions name two keys to
  one table); `HubScanHandler.LoadAsync` and `ShuttleAsync` (hub lookup shared); scan tab **Load the shuttle**,
  page `/Hub/Shuttle` with `_ShuttleParcels`, nav "Shuttle"; tabs scroll sideways on a phone.
- **Tested:** build 0 errors, no new warnings; 4 new domain tests (loaded → off the hub and bound for the delivery's
  hub, loaded again changes nothing, received at the other end clears it, order stays `AtHub`; a package on the
  shuttle counts as in when the last one arrives; not scanned in here, scanned in elsewhere, delivered from here,
  unknown package and a cancelled order refused) and 3 new integration tests (a Gulshan parcel scanned in at Uttara
  is on Uttara's manifest for Gulshan hub, loaded, on its way on Gulshan's manifest, then shelved at GUL and off
  it — shown to fail when arrival does not clear the shuttle; next-day parcels listed first, a cancelled order and
  a parcel delivered from here not listed, and the refusals; Chattogram cannot load a Dhaka label or open a Dhaka
  manifest; the page loads a parcel and the manifests show it). 154 + 6 + 81 = 241 pass, none skipped. The
  publish script rebuilt `Orders.Package` only, copying `HubId` and `ReceivedOn`; both databases published, the
  3.2 scans kept. Live on dev, phone 01745219083 in Banani (GUL hub): OD-100047 (Fashion House) and OD-100048
  (Gadget BD, fast) scanned in at MIR → "Send on to GUL hub"; MIR manifest "2 parcels to load", To Gulshan hub,
  OD-100048 (Next day) first; loaded OD-100048 → "Load for GUL hub", again → "already scanned onto the shuttle",
  OD-100044 (a Mirpur delivery) → "goes on its shelf, not the shuttle"; Gulshan's "On the way here" listed
  OD-100048 until it was scanned in at GUL ("Shelf GUL-02"). The live check caught the scan answer counting a
  loaded package as not in ("0 of 1"); now counted by `ReceivedOn`, with a test. Chattogram hub staff 404 on the
  manifest and "not found" for a Dhaka label; anonymous → login. OD-100047 left on the shuttle to GUL. Screenshots
  at 390 px fine. No errors in the app log.
- **Next:** task 3.4, riders and trips (the plan-trips job assigns Day 3 groups to riders; rider PWA screen).
- **Done (task 3.4):** `Domain/Delivery` (`Rider`, `Trip`, `TripStop`, `TripStatus`, `TripLoad`, `TripPlanner`);
  `Order.IsReadyAt`, `Order.HandToRider`; SQL schema `Delivery` with `Rider`, `Trip`, `TripStop`; EF mapping;
  `Application/Delivery` (`DeliveryParcels`, `PlanTrips/TripPlanning` + `PlanTripsJob`, `HubTrips/HubTripsHandler`,
  `RiderDay/RiderDayHandler`); recurring `plan-trips` (`Jobs:PlanTrips`, every 15 minutes); policy `Rider`; pages
  `/Hub/Trips` and `/Rider` (landing page for riders), nav "Trips" and "Today", `rider.webmanifest` and
  `Shared/_RiderApp`; demo riders in `DemoDataSeeder`; README logins.
- **Tested:** build 0 errors, no new warnings; 16 new domain tests (bike limits on parcels and weight at the
  boundary; the planner fills the first bike then the next, sends a heavy delivery to a bike with weight to spare,
  counts what a bike already carries, lets a too-big delivery wait while smaller ones go, takes late deliveries first
  and neighbours together, plans nothing without bikes; a trip takes only a closed delivery of its hub, starts once,
  is cancelled only when still planned after its day; hand-over only when every parcel is at the hub, packages leave
  it) and 5 new integration tests, each on a hub, zone and area of its own (two riders' bikes filled in order with
  one delivery left for lack of room, one with no parcel here left waiting, a not-due delivery not listed, and a second
  run changing nothing — shown to fail with the "parcel on its shelf" rule removed; the rider's stops with fee, COD,
  labels and readiness, Start trip taking out 2 deliveries and 2 orders and leaving one with nothing ready on its
  shelf, a second start refused, the hub page counting what was taken; a trip left planned yesterday cancelled and its
  delivery planned today; Chattogram finds neither the hub's trips nor the rider; the pages for hub staff and rider,
  with access denied the other way round and the login page for anonymous), plus the Hangfire round-trip test now
  covers every recurring job. The integration run caught a race that has been latent since 3.2: the pickup route test
  compares the route list with the sheet while the hub scan tests open an Uttara shop; the two classes now share an
  xUnit collection. 170 + 6 + 86 = 262 pass, none skipped (integration twice in a row). The publish script was
  reviewed first: the new schema, three tables and their indexes only (plus the usual `chk_Tenant_GroupJoinDays`
  re-create); both databases published. Live on dev (plan-trips every minute): Farhana Akter (01893456120,
  Mirpur 10) with Fashion House OD-100049 (2 parcels, COD ৳1,500) and Gadget BD OD-100050 (COD ৳800), and Nasir
  Uddin (01893456121, Pallabi) with Beauty Shop OD-100051 (fast, COD ৳650); OD-100049 and OD-100051 scanned in at
  MIR (MIR-02, MIR-03), OD-100050 left at the shop; DG-100027 and DG-100028 made due today by hand. The job logged
  "dhaka Planned 2", Chattogram 0. Trips page: Rafiq Hasan 4 of 30 parcels, DG-100027 2 of 3 here, DG-100028 1 of 1;
  Sumon Ali no trip; DG-100012 and DG-100020 (earlier hand-moved deliveries) waiting with no parcel here. Rafiq's
  Today: two stops with landmark and phone, ৳85 + ৳2,300 and ৳60 + ৳650, ৳3,095 in all, Gadget BD "Not at the hub yet" (now "Not all here yet").
  Start trip → "2 deliveries and 2 orders"; OD-100049 and OD-100051 `OutForDelivery` with packages off the hub and
  history "Out with the rider" by the rider's login, both deliveries `Dispatched` with shelves freed, OD-100050 still
  `Created`; the page then listed Fashion House only at ৳60 + ৳1,500; a second start "This trip has already left".
  A new delivery DG-100029 (OD-100052, 1 of 2 parcels on the reused shelf MIR-02) went to Sumon Ali, as Rafiq was
  out; the hub page showed Rafiq "Out, parcels taken 2 of 3". Chattogram hub staff 404 on MIR's trips; the rider is
  refused the hub pages and hub staff and a merchant the rider page; anonymous → login; Chattogram's rider sees no
  deliveries. Screenshots at 390 px (DevTools mobile emulation): no sideways scroll on either page; the live check
  led to the out-trip counts, the amber "Not all here yet" and the button spacing. No errors in the app log.
- **Next:** task 3.5, delivery screen and attempts.
- **Committed:** task 3.4 as `cbd47c1` on `day2`, pushed; pull request #1 merged 3.1–3.3 into `main`.
- **Done (market review):** Dhaka couriers' prices compared (Pathao, Steadfast, RedX, Paperfly; see Project-Context
  §1); DbUp `2026/003_DhakaFastDeliveryFee` raises OneDrop Dhaka's fast fee from ৳60 to ৳70; the unit test's copy of
  the Dhaka prices follows it. The review's other changes are planned: new task 3.4a (fast deliveries can be joined,
  Ship now as an upgrade, weight allowance, daily timetable), additions to 3.5–3.8, 4.1, new 4.7 and 4.8 (README
  and demo move to 4.9). The list of dev test orders in Project-Context is shortened (the log keeps the detail).
- **Tested:** build 0 errors, no new warnings; 170 + 6 + 86 = 262 pass, none skipped. Both databases published: the
  one DbUp script only (plus the usual `chk_Tenant_GroupJoinDays` re-create). Live (app restarted; a running app
  picks up new prices within 5 minutes, the tenant catalog's cache): Dhaka quotes fast ৳70, standard ৳60, Don't hold ৳60; Chattogram fast ৳80; a fast order
  OD-100053 (Fashion House, Banani) was charged ৳70. No errors in the app log.
- **Next:** task 3.4a, market pricing.

### 2026-09-27
- **Done:** Week 1 complete (tasks 1.1–1.6).
- **Also:** renamed all projects without the OneDrop prefix (`Courier.sln`); dropped and restored `OneDrop-Test`,
  now wired through `tests/Integration.Tests/testsettings.json`.
- **Tested:** build clean; 43 domain + 6 architecture + 13 integration tests pass; live API and portal checks on
  both tenants; publish to `OneDrop` confirmed no re-run of the seed after the rename.
- **Dev data:** Dhaka has OD-100001 (Fashion House), OD-100002 (Gadget BD) and OD-100004 (Beauty Shop) for one
  customer and address — the input for Week 2's first group.
- **Next:** Week 2, task 2.1.
