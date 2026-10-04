# How it fits together

Diagrams of the courier system as built. GitHub draws the Mermaid blocks; any Mermaid viewer works too. The words
behind them are in [Project-Context.md](Project-Context.md).

## 1. One application, four layers

A modular monolith in Clean Architecture: business rules know nothing of the database or the web, and the database
schema belongs to a SQL project, not to EF migrations.

```mermaid
flowchart TB
    subgraph Users["Who uses it"]
        Shop["Merchant: website, Facebook page, spreadsheet"]
        Public["Recipient: tracking page, SMS"]
        Staff["Hub staff, riders, courier admin"]
    end

    subgraph Web["Web (ASP.NET Core)"]
        Api["REST API /api/v1<br/>API key per merchant"]
        Pages["Razor Pages panels<br/>merchant, hub, rider, admin, public site"]
        Live["SignalR /hubs/operations<br/>live dashboards"]
    end

    subgraph App["Application"]
        Slices["Use cases, one folder each<br/>CreateParcel, BulkImport, HubScan,<br/>AssignParcels, RiderDay, RunPayouts ..."]
        Jobs["Tenant jobs<br/>payouts, texts, webhooks"]
    end

    Domain["Domain<br/>Parcel, DeliveryRate, DeliveryRun, PickupRequest,<br/>LedgerEntry, Payout, Merchant ... no packages"]

    subgraph Infra["Infrastructure"]
        Ef["EF Core AppDbContext<br/>query filters, save interceptor,<br/>tenant session, outbox writer"]
        Adapters["Adapters: SMS, payout gateway, webhooks"]
        Hangfire["Hangfire + OutboxDispatcher"]
    end

    Sql[("SQL Server<br/>schema from the SQL project + DbUp<br/>Row-Level Security")]

    Shop --> Api
    Shop --> Pages
    Public --> Pages
    Staff --> Pages
    Staff --> Live
    Api --> Slices
    Pages --> Slices
    Slices --> Domain
    Jobs --> Domain
    Hangfire --> Jobs
    Slices --> Ef
    Ef --> Sql
    Jobs --> Adapters
```

| Project | Depends on | Holds |
|---|---|---|
| `src/Domain` | nothing | entities, the parcel state machine, the rate card and charges, runs and attempts, ledger and payouts |
| `src/Application` | Domain (+ EF Core for LINQ) | use cases, jobs, interfaces for the outside world |
| `src/Infrastructure` | Application | EF mapping, tenancy, Identity, fake SMS and payout gateways, webhooks, Hangfire, demo seeder |
| `src/Web` | Infrastructure | pages, API, SignalR, composition root |
| `src/Database`, `src/Database Update` | — | the schema (dacpac) and data migrations (DbUp) |

## 2. The life of one parcel

A parcel booked in Mirpur for a recipient in Gulshan: picked up by a Mirpur rider, sent to the Gulshan hub, delivered
there, and the merchant paid the next day.

```mermaid
sequenceDiagram
    actor M as Merchant
    participant App as OneDrop
    actor PR as Pickup rider (MIR)
    participant MIR as Mirpur hub
    participant GUL as Gulshan hub
    actor DR as Delivery rider (GUL)
    actor R as Recipient

    M->>App: Book parcel (form, CSV or API) - charges from the rate card
    M->>App: Request pickup
    MIR->>App: Assign the pickup to a rider
    PR->>App: Collected (scans each parcel) - Picked up
    PR->>MIR: Brings the parcels
    MIR->>App: Receive scan - At hub
    MIR->>App: Dispatch scan to GUL - In transit
    GUL->>App: Receive scan - At hub
    GUL->>App: Assign to rider (opens the rider's run) - Out for delivery
    App-->>R: SMS: out for delivery, rider's name and phone, cash to keep ready
    DR->>R: At the door
    DR->>App: Delivered, cash collected - ledger: COD, delivery charge, COD charge
    App-->>R: SMS: delivered
    DR->>GUL: Hands in the cash
    GUL->>App: Close the run with the cash received (shortfall recorded)
    App->>M: Next day: payout of COD less charges (INV- invoice)
```

## 3. Parcel states

```mermaid
stateDiagram-v2
    [*] --> Pending: booked
    Pending --> Cancelled: merchant cancels
    Pending --> PickedUp: rider collects
    Pending --> AtHub: merchant drops it at a hub
    PickedUp --> AtHub: receive scan
    AtHub --> InTransit: dispatch to the delivery hub
    InTransit --> AtHub: receive scan
    AtHub --> OutForDelivery: assigned to a rider
    OutForDelivery --> Delivered
    OutForDelivery --> PartlyDelivered: part of the order kept
    OutForDelivery --> OnHold: not reachable, reschedule
    OnHold --> OutForDelivery: next attempt
    OutForDelivery --> Returning: refused, or the last attempt failed
    AtHub --> Returning: merchant asks for it back
    Returning --> Returned: handed back at the pickup hub
    Delivered --> [*]
    PartlyDelivered --> [*]
    Returned --> [*]
    Cancelled --> [*]
```

Where a parcel is lives beside its status: `CurrentHubId` (on a hub's shelf), `TransferToHubId` (on the way between
hubs) or `RiderId` (with a rider); at most one is set, which the database checks. A hold is allowed while attempts
remain (the courier's `MaxDeliveryAttempts`, 3); the last failed attempt sends it back. A returning parcel travels
back through the hubs to its pickup hub and is handed back there.

## 4. Money

```mermaid
flowchart LR
    Door["Rider collects cash<br/>at the door"] --> Run["Rider's run closed at the hub:<br/>cash expected vs received"]
    Door --> Ledger["Ledger lines per parcel<br/>+ COD collected<br/>- delivery charge<br/>- COD charge (1%)<br/>- return charge"]
    Ledger --> Job["Hourly payouts job:<br/>every line up to yesterday,<br/>per merchant"]
    Job --> Payout["Payout INV-000123<br/>to bKash, Nagad or bank<br/>(idempotency key)"]
    Job -. "charges more than cash" .-> Wait["Lines wait for the next payout"]
```

Charges are fixed when a parcel is booked (`DeliveryCharge`, `CodChargePercent`, `ReturnCharge` snapshot from the
rate card), so changing the rate card never reprices a parcel already booked.

## 5. Keeping couriers and merchants apart

```mermaid
flowchart TB
    Request["Request<br/>subdomain or API key"] --> Tenant["Tenant resolved<br/>(courier)"]
    Tenant --> L1["1. EF query filters<br/>TenantId, and MerchantId for merchants"]
    L1 --> L2["2. Save interceptor<br/>stamps TenantId, refuses other tenants' rows"]
    L2 --> L3["3. SQL Row-Level Security<br/>SESSION_CONTEXT per connection"]
    L3 --> Rows[("Only this courier's rows")]
```

Inside a courier, a merchant sees only its own parcels (query filter) and a rider only the parcels given to them
(each rider query filters by the signed-in rider). The fraud check is the one place that counts a phone's parcels at
every merchant, and it returns counts only, never a merchant's name. The isolation sweep test lists every route the app
maps and fails for a new one without a line saying what another courier, merchant or rider gets there.

## 6. The outbox

```mermaid
sequenceDiagram
    participant H as Handler
    participant Db as AppDbContext
    participant O as Outbox table
    participant D as OutboxDispatcher (every 5 s)
    participant S as SMS / webhook

    H->>Db: parcel.Deliver(...) raises ParcelStatusChanged
    Db->>Db: save the change
    Db->>O: write ParcelStatusChangedMessage (+ RecipientTextMessage) in the same transaction
    D->>O: due messages, oldest first
    D->>S: send (text written from the data at send time)
    S-->>D: ok, or failure: retry after 1, 2, 4, 8 minutes, then given up
```

A text that is out of date when its turn comes (an "out for delivery" text for a parcel already delivered) is not
sent. Given-up messages are listed for the admin on **Failed messages** and can be sent again.

## 7. A day at the courier

| When | Who | What |
|---|---|---|
| Morning | Merchants | Book the day's parcels, request pickups |
| Late morning | Hub staff | Assign pickups to riders; riders collect and scan |
| Afternoon | Hub staff | Receive scans; dispatch parcels for other hubs (in transit) |
| Afternoon | Hub staff | Assign parcels to riders by area: each rider's run for the day opens |
| Evening | Riders | Deliver: delivered, partly delivered, on hold (with a date), refused |
| End of run | Hub staff | Close each run: cash received against expected; held and refused parcels back on the shelf |
| Every hour | Payouts job | Pays each merchant every ledger line up to yesterday (courier's time zone) |
