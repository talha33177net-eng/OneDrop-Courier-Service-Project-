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
| Last session | 2026-10-05 — UI redesign with motion and on-page guidance; three logic gaps closed (rider stopped or moved with work, pickup point moved with parcels waiting, hold for a past day); then a new brand (logo, ink and sky blue theme) and a drawn, animated front page; all suites green, live check done |
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

### 2026-10-05
- **Owner's request:** "make the UI more beautiful … professional … add beautiful animation … make the whole system user
  friendly … also find if there is any logical gap then fix them." Done without questions (owner asleep).
- **UI:** `site.css` rewritten on the same class names: Plus Jakarta Sans and JetBrains Mono (`_Fonts.cshtml`), refined
  tokens and shadows, a glowing active-menu bar, lifted hover cards, numbers that count up, bars and progress that grow,
  a breathing "Live" dot and changed numbers flashing on live refresh, all on first paint only and off for reduced
  motion. New `site.js`: busy spinners and no double posts, a styled confirm dialog (`data-confirm`, replacing every
  `confirm()` and added to stop rider, run payouts, cancel pickup, return parcel, new webhook secret, stop webhook),
  success toasts, dismissible tips, "/" to search, show password, copy tracking code.
- **Guidance:** merchant "Get started" checklist (approval, payout account, first parcel, first handover) then a "next
  step" banner when parcels wait without a pickup; hub board as the day's six numbered steps with counts and links;
  tips on assign, runs, pickups; sidebar "how it works" per role; tracking page says in one sentence what happens next.
- **Screens:** scan page sends scans without reloading, plays a tone per result (high done, low problem; can be muted),
  keeps the session's scans and catches keystrokes for the scanner; assign page counts ticked parcels and "select all"
  skips parcels whose customer asked for a later day (marked in the row, also on the board); rider closing asks before
  closing with cash short; rider app shows day progress and a Map button, and asks for a partial-delivery reason only
  when less was collected; pickups warn when nothing is booked at the point and fill the count; booking form links a
  phone to its fraud check and flashes changed charges; sign-in is a split screen; landing page has a moving route and
  journey.
- **Logic gaps closed:** (1) stopping a rider, or moving them to another hub, while they carry parcels, have an open
  run or an assigned pickup is refused with what is open (their parcels would be stuck: the rider app shows nothing to
  a stopped rider and the run closes at the old hub). (2) A pickup point cannot move to another zone while parcels or a
  pickup wait there (they were priced and routed by the old zone). (3) `Parcel.Hold` refuses a "deliver on" day that is
  not after today; the date picker starts tomorrow.
- **Tested:** build 0 errors, 0 warnings (two old xUnit2029 warnings in `DatabaseProjectFileTests` fixed); domain 111
  (one new), architecture 6, integration 55 (two new in `GuardTests`), none skipped. Live check on `OneDrop-Test` with
  demo logins (the dev database has no demo users): every role's main pages at 1366 px and 390 px, no script errors, no
  sideways scroll; scanned five labels without reloads, assigned three parcels (toast), the stop-rider dialog and its
  refusal, a partial delivery's reason appearing. Screenshots showed the hero route crossing the text, the scan
  placeholder in capitals, the sign-in art over the role list and long demo e-mails overflowing; all fixed and
  re-checked. Six parcels for the demo shop "Fashion House" were left in `OneDrop-Test`.
- **Not done:** sound on a real hand scanner; phone screenshots of the admin pages.
- **Later the same day (owner's request):** "I want my landing page also like [Steadfast's] but not copy paste … a beautiful
  graphical cartoon type animation … change our color theme, it looks like copy of steadfast courier … make a good
  business logo." A first draft in ultraviolet was stopped by the owner: "dont use ultraviolet color, that color makes
  it feel like AI generated. Use some other color theme and the coloring minimal."
- **Brand:** logo = a drop-shaped map pin holding a taped parcel, landing on a ripple, on a marigold tile
  (`_BrandMark.cshtml` in pages; `wwwroot/images/logo.svg` and `logo-on-dark.svg` with the word outlined from
  Bricolage Grotesque; `icons/icon.svg`, `icon-192.png`, `icon-512.png` and a new full-bleed `icon-maskable-512.png`).
  Theme = ink `#17181c` on warm paper `#f7f6f3`, one accent marigold `#f2a900`; primary buttons solid ink, `btn-accent`
  for the few marketing calls to action; every gradient and glow removed; "out for delivery" is an ink badge; charts
  grey, ink and red. All of it through the tokens in `site.css`, so every panel changed with it.
- **Front page:** a little planet (shops, a hub, homes, a rickshaw, krishnachura trees, a route map on its face) turning
  under a rider on a scooter, parcels on parachutes and status cards following one parcel (`Art/_Planet.cshtml`, three
  layers so the planet turns on the compositor); a ribbon of services; four animated steps (book, pick up, hub belt
  and scanner, cash into the wallet); services; rate card (inside city in ink); a closing pin that drops on a ripple;
  a real footer. `landing.css` is loaded by the front page only; `site.js` pauses a drawing off screen and lets
  sections below the fold rise in. Tracking page uses the same tracking box.
- **Tested:** build 0 errors, 0 warnings; domain 111, architecture 6, integration 57 (two new in `PortalPagesTests`:
  the front page's drawings, tracking box and rates; every icon in the rider manifest exists), none skipped, two full
  green runs in a row. One earlier run had `/Admin` answer 500 once after 13 s in `Each_role_lands_on_its_own_home`
  (only a class name changed there; not reproduced in three more runs; looks like a database timeout under parallel
  load). Live: front page at 1440 px and 390 px (no sideways scroll, no script errors), merchant, admin and hub panels
  at 1440 px, rider app at 390 px, sign-in, sign-up and tracking on `OneDrop-Test` with demo logins. Screenshots showed
  birds flying over the headline, a gap after the price in rate lists (old), the tracking progress bar turned into
  cards by a clashing `.step` class, the tracking placeholder cut off on phones and the hub tip still saying "orange";
  all fixed and re-checked.
- **Not done:** a look on a real phone and in Safari; the intermittent `/Admin` 500 is not explained.
- **Then (owner's review):** "I don't like the yellow color theme … try something light color like sky blue." The
  accent is now sky blue `#7cc6f2` (`--accent-text` `#0b6a9e` for links and text, `--accent-soft` `#d9effc`), and the
  neutrals moved from warm paper to cool light grey (`--bg` `#f5f7fa`, ink `#141820`) because cream beside sky blue
  looked muddy in screenshots. Logo tile, app icons and logo files regenerated in sky blue; drawings' second tone light
  sky `#c6e7fb`; the sun is white with sky rays; the dev strip and the "needs you" stat cards follow the accent; the
  hub tip says "a blue number". Tested: build 0 warnings; 111 + 6 + 57 pass, none skipped; live at 1440 px and 390 px
  (front page, merchant panel), no sideways scroll or script errors.
- **Then:** the owner asked for a more professional headline. Now "Reliable delivery. Next-day payouts." (58 px, was
  62 px) with a plainer lead and the label "Courier service for online businesses"; checked at 1440 px and 390 px.

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
- **Later the same day (owner's review):** branch pushed and fast-forwarded into local `main` (not pushed). Demo data
  removed from the dev database (parcels, riders, demo merchants and logins; hubs, zones, areas, rates, both admins and
  the owner's own sign-up kept) and `Seed:DemoData`/`DemoActivity` turned off in `appsettings.Development.json`.
  The owner found the admin's "Pickups open" tile opened Barishal (first by name) while the pickup was at Mirpur.
  Locking the admin to one hub was considered and rejected (the admin must see the whole courier); instead hub pages
  ask "Choose your hub" (every hub with its waiting work, busiest first) until one is chosen, the choice is forgotten
  at sign-in and sign-out, the dashboard tiles open the one hub with that work (or the chooser), and the hub menu shows
  each hub's waiting count with an "All hubs" link. Tested: build 0 warnings; 109 + 6 + 51 = 166 pass; live on dev as
  admin: `/Hub/Pickups` → chooser, "Pickups open" → Mirpur directly, menu shows "Mirpur hub (MIR) · 1 waiting".
  Not done: screenshots at phone width.
- **Later still (owner's review):** a parcel is now texted a tracking link the moment it is booked, not only when it goes
  out for delivery, so the recipient can follow it from the start. `Parcel.Create` raises a new `ParcelBooked` event,
  kept separate from `ParcelStatusChanged` so booking never posts to the merchant's webhook (that still starts at
  pickup); the outbox writes it as the existing `RecipientTextMessage` with status `Pending`, and `RecipientTexts`
  gained a case for it (dropped if the parcel has already moved on by the time it is sent, same as the others).
  Tested: build 0 warnings; 110 + 6 + 52 = 168 pass (one new domain test, one new and one adjusted integration test);
  live on dev: booked a parcel as a merchant, the tracking SMS appeared in `/Dev/Sms` with a working `/Track` link.
  Test merchant and parcel removed afterwards.
- **Next:** the owner's review (2.1).
