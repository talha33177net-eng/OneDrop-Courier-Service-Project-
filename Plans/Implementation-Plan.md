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
| Current week | **Week 2 — Grouping core** (in progress, 3 of 9) |
| Next task | 2.4 Quote endpoint |
| Last session | 2026-09-28 — tasks 2.1–2.3: delivery groups, grouping on create, pricing from the tenant's settings (committed on `day2`) |
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
| 2 | Grouping core | 3 shops' orders form 1 group | 🟡 In progress (2.1–2.3 done) |
| 3 | Operations and money | Group delivered, merchants settled | ⬜ |
| 4 | Polish and proof | Full demo runs end to end | ⬜ |

Tests today: **124 passing** (87 domain, 6 architecture, 31 integration).

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

## Week 2 — Grouping core ⬜

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
- [ ] **2.4 Quote endpoint.** `GET /api/v1/quote?phone=&area=&line1=` → "৳60" or "+৳25" for the checkout, without
      revealing which other shops are in the group (merchant privacy). *Tests:* quote matches the fee on create.
- [ ] **2.5 Hangfire and the lock job.** Hangfire on SQL Server; jobs take the tenant as a parameter.
      `LockDueGroups` every 5 minutes. *Tests:* job locks due groups only, per tenant; an order after the lock
      opens a new group.
- [ ] **2.6 Ship now.** Customer page button and API; locks the group immediately. *Tests:* only the owning
      customer can do it; a locked group rejects it.
- [ ] **2.7 Outbox and domain events.** `Notifications.OutboxMessage` saved in the same transaction; sender job
      every few seconds; SMS "joined your delivery", "group locked, arriving Day 3". *Tests:* event saved with the
      order; sender marks it sent; failure retries.
- [ ] **2.8 Customer group page (PWA).** "My deliveries" shows the group: shops, packages collected, lock time,
      delivery day, fee so far, "You save ৳70" versus separate couriers. Web manifest so it installs on a phone.
      *Tests:* integration test for the page model; live check on a phone-width window.
- [ ] **2.9 Week 2 demo run.** The three Dhaka demo shops → one group → lock → ৳110. Update this file.

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
| Every few seconds | Outbox sender | 2 |
| Every 5 minutes | Lock check (lock due groups) | 2 |
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
- **Next:** task 2.4, the checkout quote (reuses `DeliveryFeeCalculator.AddedFee`).

### 2026-09-27
- **Done:** Week 1 complete (tasks 1.1–1.6).
- **Also:** renamed all projects without the OneDrop prefix (`Courier.sln`); dropped and restored `OneDrop-Test`,
  now wired through `tests/Integration.Tests/testsettings.json`.
- **Tested:** build clean; 43 domain + 6 architecture + 13 integration tests pass; live API and portal checks on
  both tenants; publish to `OneDrop` confirmed no re-run of the seed after the rename.
- **Dev data:** Dhaka has OD-100001 (Fashion House), OD-100002 (Gadget BD) and OD-100004 (Beauty Shop) for one
  customer and address — the input for Week 2's first group.
- **Next:** Week 2, task 2.1.
