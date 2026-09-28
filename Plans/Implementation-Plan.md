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
| Current week | **Week 3 — Operations and money** (not started, 0 of 9) |
| Next task | 3.1 Pickup routes and QR labels |
| Last session | 2026-09-28 — tasks 2.1–2.8 committed on `day2` and pushed to `main`; 2.9 demo run passed (uncommitted), Week 2 done |
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
| 3 | Operations and money | Group delivered, merchants settled | ⬜ |
| 4 | Polish and proof | Full demo runs end to end | ⬜ |

Tests today: **188 passing** (115 domain, 6 architecture, 67 integration).

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
- **Deliver fast** (next day, ৳60, no waiting) and **Don't hold** items skip the group.
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

- [ ] **3.1 Pickup routes and QR labels.** `Network.Route` per zone with a daily time (tenant setting); route
      stops = merchants with parcels; label `OD-100001-1` as a QR code on a printable page. *Tests:* route
      includes only that zone's merchants with waiting parcels.
- [ ] **3.2 Hub scan-in and shelves.** Hub staff scan a label → order `AtHub`; each open group gets a labelled
      shelf. *Tests:* scanning another tenant's label is rejected; statuses move correctly.
- [ ] **3.3 Hub shuttle.** Parcels picked up in another zone travel to the customer's zone hub. *Cut option:*
      a manual "transfer" button.
- [ ] **3.4 Riders and trips.** `Delivery.Rider`, `Delivery.Trip`; the plan-trips job assigns Day 3 groups to
      riders; per-bike limit. Rider PWA screen: today's stops.
- [ ] **3.5 Delivery screen and attempts.** Handover only after the fee is paid ("no fee, no handover"); refuse
      one parcel (fee counts accepted shops only); not home = one free re-attempt, then return.
- [ ] **3.6 Door payment.** Collect fee + COD, cash or QR (bKash/Nagad adapter, fake in MVP). SMS receipt.
      New and low-trust customers pay the fee in advance by payment link.
- [ ] **3.7 Ledger and next-day settlement.** Split every payment per merchant; the settle job pays yesterday's
      COD to each merchant (fake bKash/bank). Rider end-of-day cash deposit and check. *Tests:* ledger balances;
      each merchant gets exactly its COD.
- [ ] **3.8 Trust score (simple).** Refusals and no-shows lower it; merchants' late handovers lower theirs.
      *Cut option:* a simple refusal counter.
- [ ] **3.9 Week 3 demo run.** Group delivered, merchants settled.

---

## Week 4 — Polish and proof ⬜

**Done when:** the final demo script below runs end to end.

- [ ] **4.1 Dashboards (SignalR).** Live counts for admin and hub: open groups, parcels at hub, riders out.
- [ ] **4.2 Merchant webhooks.** Order status changes posted to the merchant's URL through the outbox, signed.
- [ ] **4.3 Tenant isolation test sweep.** Every endpoint and page: tenant A gets 404 for tenant B; merchant sees
      only its parcels.
- [ ] **4.4 SQL Server Row-Level Security** (isolation layer 3), using SESSION_CONTEXT set per connection.
      *Cut option:* moves to "next step"; EF filters and the save interceptor still protect the data.
- [ ] **4.5 Docker and CI.** `docker-compose.yml` (app + SQL Server); GitHub Actions: build, publish the dacpac
      to a throwaway database, run all tests.
- [ ] **4.6 Simulator.** `tools/Simulator` fills both tenants with fake merchants and orders for demos.
- [ ] **4.7 README, diagrams, demo.** Final documentation and a recorded demo run.

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
| 08:00 | Plan delivery trips (Day 3 groups → riders) | 3 |
| 13:00 | Build pickup routes per zone | 3 |
| 14:00 | Pickup route runs (merchants → hub) | 3 |
| 19:00 | Hub shuttle (zone → customer's hub) | 3 |
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

### 2026-09-27
- **Done:** Week 1 complete (tasks 1.1–1.6).
- **Also:** renamed all projects without the OneDrop prefix (`Courier.sln`); dropped and restored `OneDrop-Test`,
  now wired through `tests/Integration.Tests/testsettings.json`.
- **Tested:** build clean; 43 domain + 6 architecture + 13 integration tests pass; live API and portal checks on
  both tenants; publish to `OneDrop` confirmed no re-run of the seed after the rename.
- **Dev data:** Dhaka has OD-100001 (Fashion House), OD-100002 (Gadget BD) and OD-100004 (Beauty Shop) for one
  customer and address — the input for Week 2's first group.
- **Next:** Week 2, task 2.1.
