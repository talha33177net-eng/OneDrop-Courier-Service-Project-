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
| Current week | **Week 2 — Grouping core** (not started) |
| Next task | 2.1 Delivery group entity and table |
| Last session | 2026-09-27 — Week 1 finished, projects renamed, test database restored |
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
| 2 | Grouping core | 3 shops' orders form 1 group | ⬜ Next |
| 3 | Operations and money | Group delivered, merchants settled | ⬜ |
| 4 | Polish and proof | Full demo runs end to end | ⬜ |

Tests today: **62 passing** (43 domain, 6 architecture, 13 integration).

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
- [ ] Create Order returns the fee (৳60 / +৳25) → task 2.3.
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
- [ ] **2.1 Delivery group entity and table.** `Grouping.DeliveryGroup` (TenantId, CustomerId, AddressId, HubId,
      Number DG-…, Status Open/Locked/Dispatched/Delivered/Cancelled, OpenedOn, LocksAt, LockedOn, RowVersion).
      Filtered unique index: one `Open` group per (CustomerId, AddressId). `Orders.Order.DeliveryGroupId`.
      *Tests:* state machine unit tests; schema-match test covers the new table.
- [ ] **2.2 Grouping rule in Create Order.** Find the open group or open a new one, inside the same save as the
      order. Deliver fast / Don't hold get their own single-order group. Concurrency: the unique index + retry.
      *Tests:* Day 1 and Day 2 join, Day 3 starts a new group (FakeTimeProvider); home vs office = 2 groups;
      parallel orders for a new customer = 1 group (integration).
- [ ] **2.3 Pricing.** One `DeliveryFeeCalculator` used everywhere (strategy per tenant settings). Create Order
      returns `fee` and `groupFee`. *Tests:* ৳60 / ৳85 / ৳110 / ৳135 for 1–4 shops; two orders from the same shop
      count once; Chattogram prices differ.
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
| An order on Day 3 starts a new group | ⬜ Week 2 |
| Fee = ৳60 + ৳25 per distinct accepted shop | ⬜ Week 2 |
| Only one open group per customer + address, even with two orders at the same moment | ⬜ Week 2 (one customer: ✅) |
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

### 2026-09-27
- **Done:** Week 1 complete (tasks 1.1–1.6).
- **Also:** renamed all projects without the OneDrop prefix (`Courier.sln`); dropped and restored `OneDrop-Test`,
  now wired through `tests/Integration.Tests/testsettings.json`.
- **Tested:** build clean; 43 domain + 6 architecture + 13 integration tests pass; live API and portal checks on
  both tenants; publish to `OneDrop` confirmed no re-run of the seed after the rename.
- **Dev data:** Dhaka has OD-100001 (Fashion House), OD-100002 (Gadget BD) and OD-100004 (Beauty Shop) for one
  customer and address — the input for Week 2's first group.
- **Next:** Week 2, task 2.1.
