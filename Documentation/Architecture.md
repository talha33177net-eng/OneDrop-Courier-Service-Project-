# How it fits together

Diagrams of the system as built at the end of the 4-week MVP. GitHub draws the Mermaid blocks; any Mermaid viewer
works too. The words behind them are in [Project-Context.md](Project-Context.md).

## 1. One application, four layers

A modular monolith in Clean Architecture: business rules know nothing of the database or the web, and the database
schema belongs to a SQL project, not to EF migrations.

```mermaid
flowchart TB
    subgraph Users["Who uses it"]
        Shop["Shop website or Facebook seller"]
        Customer["Customer on a phone"]
        Staff["Hub staff, riders, operator admin"]
    end

    subgraph Web["Web (ASP.NET Core)"]
        Api["REST API /api/v1<br/>API key per shop"]
        Pages["Razor Pages portals<br/>shop, customer, hub, rider, admin"]
        Live["SignalR /hubs/operations<br/>live dashboards"]
    end

    subgraph App["Application"]
        Slices["Use cases, one folder each<br/>CreateOrder, DeliveryGrouping, HubScan,<br/>Door, SettleMerchants, ShopWindow ..."]
        Jobs["Tenant jobs<br/>lock, plan trips, settle, outbox"]
    end

    Domain["Domain<br/>Order, DeliveryGroup, Trip, Payment, LedgerEntry,<br/>DeliveryFeeCalculator ... no packages"]

    subgraph Infra["Infrastructure"]
        Ef["EF Core AppDbContext<br/>query filters, save interceptor,<br/>tenant session, outbox writer"]
        Adapters["Adapters: SMS, payment gateway,<br/>payout gateway, webhooks"]
        Hangfire["Hangfire + OutboxDispatcher"]
    end

    Sql[("SQL Server<br/>schema from the SQL project + DbUp<br/>Row-Level Security")]

    Shop --> Api
    Customer --> Pages
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
| `src/Domain` | nothing | entities, state machines, fee calculator, trust and drop-off rules |
| `src/Application` | Domain (+ EF Core for LINQ) | use cases, jobs, interfaces for the outside world |
| `src/Infrastructure` | Application | EF mapping, tenancy, Identity, fake SMS/payment/payout gateways, webhooks, Hangfire |
| `src/Web` | Infrastructure | pages, API, SignalR, composition root |
| `src/Database`, `src/Database Update` | — | the schema (dacpac) and data migrations (DbUp) |

The project references allow only these directions, so a business rule cannot reach the database or the web.
`tests/Architecture.Tests` fails when an entity outside `Platform` has no `TenantId`, or a SQL file is missing from the
SQL project or its standard header.

## 2. The life of one delivery

Two shops, one customer, one rider, one fee. The money at the door is the delivery fee (OneDrop's) and the cash on
delivery (the shops'); the shops are paid their COD the next day.

```mermaid
sequenceDiagram
    autonumber
    participant A as Shop A
    participant B as Shop B
    participant OD as OneDrop
    participant C as Customer
    participant H as Hub
    participant R as Rider

    A->>OD: Quote, then Create Order (phone + address)
    OD-->>A: fee ৳60
    OD->>C: SMS "joined delivery DG-…, add from any OneDrop shop for +৳25"
    B->>OD: Create Order, same phone + address
    OD-->>B: fee ৳25 (never told about Shop A)
    C->>OD: optional Ship now (deliver tomorrow, +৳10 on Day 1)
    Note over OD: End of Day 2, or Ship now: the delivery locks
    H->>A: pickup route collects (scan "Collect")
    H->>B: pickup route collects
    H->>H: scan "Receive": both parcels on one shelf (MIR-04)
    OD->>R: plan trips: the delivery on a rider's trip
    R->>H: Start trip: takes every order whose parcels are all here
    R->>C: one stop: fee ৳95 + COD, cash or bKash/Nagad QR
    C-->>R: pays exactly what is due ("no fee, no handover")
    OD->>C: SMS receipt
    R->>H: hands in the day's cash
    Note over OD: Next morning, settle job
    OD->>A: payout = COD - charges
    OD->>B: payout = COD - charges
```

## 3. Delivery states

A delivery group opens with the customer's first order to an address and is the unit the hub shelves and the rider
delivers. Orders have their own states (`Created` → `PickedUp` → `AtHub` → `OutForDelivery` → `Delivered`, or
`Refused` → `ReturnedToMerchant`, or `Cancelled`).

```mermaid
stateDiagram-v2
    [*] --> Open: first waiting order
    [*] --> Locked: Deliver fast or Don't hold order (next day)
    Open --> Locked: end of Day 2, or Ship now
    Open --> Cancelled: combined into another delivery
    Locked --> Cancelled: combined into another delivery
    Locked --> Dispatched: rider presses Start trip
    Dispatched --> Delivered: handed over and paid
    Dispatched --> Locked: nobody home (one free re-attempt)
    Dispatched --> Cancelled: everything refused, or nobody home twice
    Delivered --> [*]
    Cancelled --> [*]
```

`Kind` tells what a locked delivery may still take: `Waiting` (the 3-day kind) takes nothing once locked;
`NextDay`, `ShippedNow` and `FollowUp` still take the customer's orders while the new order's pickup route reaches the
hub before the delivery's trip.

## 4. Keeping operators apart

Three layers, so a bug in one is still caught by the next. Inside an operator a fourth rule hides each shop's orders
from every other shop.

```mermaid
flowchart LR
    Req["Request"] --> Resolve{"Tenant from<br/>subdomain or API key"}
    Resolve --> L1["1. EF query filters<br/>WHERE TenantId = current<br/>and MerchantId = the shop"]
    L1 --> L2["2. Save interceptor<br/>stamps TenantId,<br/>refuses another tenant's rows"]
    L2 --> L3["3. Row-Level Security<br/>SESSION_CONTEXT TenantId<br/>on every app connection"]
    L3 --> Db[("Rows of this operator only")]
```

| Check | Where |
|---|---|
| Every route answers another operator or shop with nothing of this one's | `tests/Integration.Tests/TripTests.Isolation.cs` (a new route fails until it has its line) |
| Every tenant table is in the security policy | `RowLevelSecurityTests` |
| Every entity outside `Platform` carries a `TenantId` | `Architecture.Tests` |

## 5. Side effects through the outbox

Texts and webhooks never run inside the use case. The change and its message are saved in one transaction, then sent
by a loop every 5 seconds, retried after 1, 2, 4 and 8 minutes.

```mermaid
flowchart LR
    UseCase["Use case<br/>e.g. Create Order"] -->|"entity raises<br/>a domain event"| Save["AppDbContext.SaveChangesAsync<br/>one transaction"]
    Save --> Rows[("Business rows")]
    Save --> Outbox[("Notifications.OutboxMessage")]
    Outbox --> Texts["SendOutboxJob<br/>customer SMS"]
    Outbox --> Hooks["SendWebhooksJob<br/>signed shop webhooks"]
    Save -->|"after commit"| Feed["IOperationsFeed<br/>'changed' to live dashboards"]
```

## 6. A day at an operator

Times are the tenant's own (Dhaka shown).

| Time | What | How |
|---|---|---|
| All day | Orders arrive and join deliveries | API or the shop's New order page |
| Every 5 s | Customer texts, shop webhooks | outbox loops |
| Every 5 min | Deliveries past their deadline lock | `lock-due-groups` job |
| 11:00–13:30 | Pickup routes, farthest zones first | route sheet per zone |
| About 14:30 | Hub shuttle to the customer's hub | shuttle manifest, load and receive scans |
| From midnight, every 15 min | Deliveries due today go onto riders' trips | `plan-trips` job, or **Plan trips now** |
| 17:00–21:00 | Riders deliver, one stop per customer and area | rider's **Today** |
| Evening | Riders hand in cash | hub **Cash** |
| Every hour | Shops paid everything up to yesterday | `settle-merchants` job |
