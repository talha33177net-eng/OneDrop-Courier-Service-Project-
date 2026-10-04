# Implementation plan and progress tracker

The one place to see where we are and what comes next for **OneDrop Courier**, the traditional courier system
(pickup, hubs, delivery, cash on delivery, next-day payouts). The grouped-delivery plan it replaces (2026-09-27 to
2026-10-04) is in git history up to commit `3b48bc9`.

**Start of every session:** read [Today](#today), then take the first unchecked task.
**End of every task:** run the [test routine](#test-routine-after-every-task), tick the box, add a line to the
[daily log](#daily-log).

---

## Today

| | |
|---|---|
| Current phase | **Phase 2 — After the rebuild** |
| Next task | Owner's review of the rebuild on branch `traditional-courier` (uncommitted), then 2.1 |
| Last session | 2026-10-04 — the pivot: grouping removed, the courier rebuilt (domain, schema, seed, every panel, new UI), both databases reset, all suites green, live check done |
| Blockers | None |

---

## Test routine (after every task)

A task is **not done** until all of these pass. Record the result in the daily log.

1. **Build:** `dotnet build Courier.sln` shows 0 errors and no warnings.
2. **New tests:** every new rule gets a unit test in `tests/Domain.Tests`. Anything touching the database, the
   API, a page or tenancy gets an integration test in `tests/Integration.Tests` (and its line in the isolation sweep
   for a new route).
3. **Schema changed?** `./tools/db/publish.ps1` (dev) **and** `./tools/db/publish.ps1 -Database OneDrop-Test`.
4. **All suites green:**
   ```powershell
   dotnet test --project tests/Domain.Tests
   dotnet test --project tests/Architecture.Tests
   dotnet test --project tests/Integration.Tests     # must say succeeded, not skipped
   ```
5. **Live check:** `dotnet run --project src/Web`, then use the feature on http://onedrop.localhost:5080 at desktop and
   phone width. Check another merchant, rider or courier cannot see it.
6. **Update this file:** tick the task, note anything deferred, update [Today](#today).

---

## Phase 1 — The rebuild as a traditional courier ✅ (2026-10-04)

The owner's request: "Forget the group delivery system … make this project just like the traditional delivery
systems of Bangladesh like Steadfast … make the UI normal, minimal and good looking … make every user panel and the
admin panel better." Owner's answers: start from the last commit (stash the uncommitted work), keep the multi-tenant
base with one courier seeded, reset both databases, recipients track publicly with no accounts.

- [x] **1.1 Domain.** Grouping, customers, orders, trips, shelves, door payments, settlements, trust and drop-off rules
      removed. New: `Parcel` (state machine, location fields, charges snapshot, events), `ServiceArea` and
      `DeliveryRate`, `Merchant` (status, payout account), `PickupPoint`, `Rider`, `DeliveryRun`, `DeliveryAttempt`,
      `PickupRequest`, `LedgerEntry`, `Payout`; `PhoneNumber` moved to `Domain.Common`; `Tenant` keeps only name, slug,
      time zone, currency, SMS sender, hotline and maximum attempts.
- [x] **1.2 Schema and seed.** SQL project rewritten (schemas `Parcels`, `Pricing`; `Delivery`, `Payments`, `Network`,
      `Merchants` reshaped), sequences `TrackingNumber` and `PayoutNumber`, the security policy on all 17 tenant tables.
      DbUp history replaced by `001_SeedCourier.sql` (16 hubs, 21 zones, 87 areas, the rate card). Both databases
      dropped and deployed from nothing.
- [x] **1.3 Application.** Booking (API, form, CSV), browse and details, actions (edit, cancel, return), labels,
      tracking, quote, fraud check; hub scan, board, assign, runs; pickups; rider day; riders; merchant onboarding,
      admin, account; payouts job, merchant payments, admin payouts; rates; coverage; dashboards; outbox contracts,
      recipient texts, webhooks, failed messages.
- [x] **1.4 Infrastructure.** Mappings, `AppDbContext`, Identity without customers, tracking links, user accounts, fake
      payout gateway, the demo seeder (logins, merchants, riders, keys) and `DemoActivity` (sample parcels in every
      stage in Development), Hangfire job `merchant-payouts`.
- [x] **1.5 UI.** A new design system (`site.css`: tokens, dark sidebar shell, cards, stats, badges, tables, tabs,
      timeline, charts, public site, rider mobile, print labels) with inline SVG icons; public site, sign-in and
      sign-up; merchant, hub, rider, admin, platform and Dev panels rebuilt.
- [x] **1.6 API.** `POST/GET /api/v1/parcels`, `POST /api/v1/parcels/{code}/cancel`, `GET /api/v1/charge`,
      `GET /api/v1/areas`.
- [x] **1.7 Tests.** Domain tests rewritten (parcels, pricing, money, delivery, merchants, webhooks, outbox); integration
      tests rewritten (parcel API, merchant panel, delivery flows, payouts, webhooks, texts, failed messages, portal
      pages, simulator, isolation sweep over every route, wrong-host sweep, Row-Level Security, save interceptor).
- [x] **1.8 Simulator, docs.** Simulator books parcels; README, Project-Context, Architecture, Demo, Conventions,
      Database rewritten for the courier.

---

## Phase 2 — After the rebuild

- [ ] **2.1 Owner's review and commit** on `traditional-courier`; merge into `main` when approved.
- [ ] **2.2 Coverage admin.** Add and edit hubs, zones (city, suburb) and areas on a page instead of the seed.
- [ ] **2.3 Run sheet.** A printable list of a rider's parcels (address, phone, COD) when the run is assigned.
- [ ] **2.4 Merchant notifications.** Tell the merchant by SMS or webhook when a payout is sent.
- [ ] **2.5 Reports.** COD collected, charges and shortfalls by day and by rider; returns by merchant.
- [ ] **2.6 Real gateways.** SMS, bKash / Nagad / bank payout adapters behind the existing interfaces.
- [ ] **2.7 The app's own SQL login** without `ALTER ANY SECURITY POLICY` (shown to the owner before it is created).

---

## Background jobs

| When | Job |
|---|---|
| Every 5 seconds (`Jobs:OutboxInterval`) | Outbox senders in process: recipient texts, and merchants' webhooks in a loop of their own |
| Every hour (`Jobs:Payouts`) | `merchant-payouts`: every merchant's ledger lines up to yesterday (courier's day) |

## Must-pass tests

| Test | Where |
|---|---|
| Charges follow the rate card per service area and weight; COD charge rounded | `PricingTests`, `ParcelApiTests` |
| A parcel moves only along its state machine; at most one location | `ParcelTests` |
| The last failed attempt returns the parcel | `ParcelTests`, `DeliveryFlowTests` |
| A merchant sees only its own parcels; a rider only theirs; another courier nothing | `IsolationTests` (every route), `ParcelApiTests`, `RowLevelSecurityTests` |
| A payout pays each line once and carries charges larger than the cash | `PayoutTests`, `MoneyTests` |
| Every entity (except Platform) has a TenantId | `TenantOwnershipTests` |

---

## Decisions log

The decisions and their reasons are in [Project-Context.md §4](../Documentation/Project-Context.md#4-decisions-and-the-reasons-for-them).
New ones are added there and dated here.

| Date | Decision |
|---|---|
| 2026-10-04 | Pivot to a traditional courier; start from `3b48bc9`, stash the uncommitted Week 5 rewrite; keep multi-tenancy with one courier; reset both databases; public tracking only (owner) |
| 2026-10-04 | SQL Server reached at `10.50.0.1,1433` over the VPN instead of `ras-x2` (owner) |

---

## Daily log

Newest first.

### 2026-10-04
- **Decided with the owner:** the pivot above.
- **Done:** phase 1 (tasks 1.1–1.8). Both databases dropped (no sessions open) and deployed from nothing with the new
  dacpac and seed.
- **Tested:** build 0 errors, 0 warnings; 109 domain + 6 architecture + 50 integration = 165 pass, none skipped.
  The integration run found two EF queries that grouped then joined (admin payouts, admin dashboard: 500), now
  aggregated first; public tracking showed the collected amount through an event note, now each step shows its status
  only; the out-for-delivery text is correctly skipped once the parcel is delivered (the test now sends between
  steps). Live on dev: every page answered 200 for its role (public, merchant, hub, rider, admin, Dev), the sample
  parcels' texts went out, no errors in the log. Screenshots at 1366 px and 390 px (DevTools emulation) of 15 pages,
  no sideways scroll at phone width; they showed the app shell broken on desktop (the menu scrim label took a grid
  cell), the search icon over its placeholder, the brand label wrapping, a poor ৳ glyph, a tiny tracking-code heading,
  the rider's phone in +880 form and "Attempt 0 of 3" while out; all fixed and re-checked.
- **Not done:** a mutation run of the isolation sweep; scanning a printed label with a real hand scanner or phone.
- **Next:** the owner's review (2.1).
