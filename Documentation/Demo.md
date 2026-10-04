# The final demo

The plan's "done when" for the MVP: two shops' orders for one customer travel as one delivery, the rider collects
the fee and the cash on delivery, both shops are paid the next morning, and a second operator sees none of it. This
page is the script to present it, followed by the run recorded on 2026-10-04.

## Before you start

```powershell
./tools/db/publish.ps1
dotnet run --project src/Web -- --Jobs:SettleMerchants="* * * * *"
```

The extra argument runs the payout job every minute instead of every hour. Sign-in pages in Development show every
demo login as a button (password `OneDrop#2026`). Open the **SMS inbox** from the Demo mode bar in a second tab: every
text the customer gets lands there.

A real delivery takes three days and pays out the morning after. Two shortcuts compress that into one sitting; both
are plain SQL against the dev database `OneDrop`, shown in step 5 and step 6.

## The script

| Step | Who and where | What to show |
|---|---|---|
| 1 | Operator admin, http://dhaka.localhost:5080 → **Dashboard** | OneDrop Dhaka's hubs live, packages per delivery by area |
| 2 | Two shops, by API (or **New order** on each shop's page) | Same phone and address: the first order ৳60, the second +৳25. The second shop is never told about the first |
| 3 | Customer: **Track my delivery** → sign in with the phone (code in the SMS inbox) → **My deliveries** | One delivery with both shops, "You save ৳35", the shopping window. **Deliver tomorrow for +৳10** (Ship now) |
| 4 | Hub staff, **Scan** at Mirpur hub: **Collect at the shop**, then **Receive at the hub** for each label | Both parcels go to the same shelf; **Shelves** shows it Ready |
| 5 | Hub staff, **Trips** → **Plan trips now**; rider (`rider@dhaka`) → **Today** → **Start trip** → the stop → **Collected ৳…, hand over** | One stop, "Collect ৳2,095": fee ৳95 + COD ৳2,000; the SMS receipt |
| 6 | Hub staff, **Cash** → record the rider's cash. Next morning, each shop → **Payouts** | Each shop paid exactly its COD; neither sees the other's order |
| 7 | Same phone at http://chattogram.localhost:5080 | Its own price (৳70), a new delivery, none of Dhaka's data |

The fee is ৳95, not the ৳85 of the original plan: since the market review, Ship now on Day 1 brings delivery forward
and adds the fast difference (Dhaka ৳70 − ৳60). Pressed on Day 2 it is free and the fee stays ৳85.

### Shortcut for step 5: make today the delivery day

Ship now delivers tomorrow. To deliver today, move the delivery's dates back one day:

```sql
UPDATE Grouping.DeliveryGroup
SET    OpenedOn = DATEADD(DAY, -1, OpenedOn),
       LocksAt  = DATEADD(DAY, -1, LocksAt),
       LockedOn = DATEADD(DAY, -1, LockedOn)
WHERE  Number = 'DG-100095';      -- your delivery's number
```

### Shortcut for step 6: make it tomorrow for the payout

Payouts take every line up to yesterday. Date the delivery's ledger lines yesterday; the job pays within a minute:

```sql
UPDATE l
SET    EntryDate = DATEADD(DAY, -1, EntryDate)
FROM   Payments.LedgerEntry l
       JOIN Orders.[Order] o ON o.Id = l.OrderId
WHERE  o.Number IN ('OD-100155', 'OD-100156') AND
       l.SettlementId IS NULL;
```

The fake payout gateway lists what it sent on **Wallet payments** (`/Dev/Payments`).

## Recorded run, 2026-10-04

On the dev database, driven through the running app's pages and API (curl), with the two shortcuts above.

1. **Admin:** `admin@dhaka.onedrop.test` signed in.
2. **Two shops:** for Taslima Rahman, 01819274151, House 9, Road 4, Mirpur 10:
   - Fashion House: quote `{ fee: 60, joinsDelivery: false }`, then **OD-100155**, fee ৳60, COD ৳1,200;
   - Gadget BD: quote `{ fee: 25, joinsDelivery: true }`, then **OD-100156**, fee ৳25, COD ৳800.

   Both answered `waitsFor: confirm` (a new cash customer). The texts "Is your Fashion House order OD-100155
   correct? Confirm it and we deliver on Tue 6 Oct: …/Customer/Order?token=…" were each confirmed from their link:
   "your order is confirmed. We deliver it on Tuesday 6 October".
3. **Customer:** signed in by SMS code. "My deliveries": **DG-100095**, arriving Tuesday 6 October, other shops can
   join until the end of Monday 5 October, Fashion House OD-100155 and Gadget BD OD-100156, 0 of 2 collected, delivery
   fee so far ৳85, COD ৳2,000, ৳2,085 at the door, "You save ৳35 against 2 separate deliveries", "Deliver tomorrow
   for +৳10". Pressed: "Delivery DG-100095 is closed. We deliver it on Monday 5 October. Its fee went up by ৳10";
   fee ৳95, ৳2,095 at the door.
4. **Hub:** `hub@dhaka` at MIR. Collect: "Collected OD-100155 from Fashion House: bring all 1 package to the hub", the
   same for OD-100156. Receive: "Shelf MIR-04 … Order OD-100155: 1 of 1 package in · Delivery DG-100095, Mon 5 Oct",
   then OD-100156 onto MIR-04. **Shelves:** MIR-04, DG-100095, 2 orders, 2 of 2, Ready.
5. **Rider:** shortcut applied (delivery day Sun 4 Oct). **Plan trips now** put DG-100095 on Rafiq Hasan's trip, with
   two older test deliveries waiting at Mirpur (DG-100064, DG-100069). `rider@dhaka`: **Start trip**; the stop for
   Taslima Rahman: fee ৳95, COD ৳2,000, "Collect ৳2,095"; cash ৳2,095 → "Handed over. Collected ৳2,095 in cash".
   Receipt: "OneDrop receipt: ৳2,095 paid in cash on Sun 4 Oct for delivery DG-100095. Delivery fee ৳95. Fashion
   House OD-100155 ৳1,200, Gadget BD OD-100156 ৳800. Thank you." The two old deliveries were recorded "nobody home"
   (back to the hub for a free re-attempt); "Every stop is done".
6. **Cash and payouts:** hub **Cash**: "Rafiq Hasan handed in ৳2,095, all the cash collected". Ledger: Fashion House
   +৳1,200 and Gadget BD +৳800 (COD of OD-100155 and OD-100156), dated yesterday by the shortcut. The job: "dhaka
   Settled up to 10/03/2026: 2 payouts, 2000.00 sent, 0 carried forward" — settlement 6, ৳1,200 to Fashion House's
   01711000001, settlement 7, ৳800 to Gadget BD's 01711000002, both `Paid`. Gadget BD's **Payouts** page does not
   mention OD-100155, and Gadget BD's API key gets 404 on it.
7. **Chattogram:** the same phone quoted `{ fee: 70, joinsDelivery: false }`; Fashion House Chattogram's OD-100157 ৳70
   opened DG-100097. Signed in at Chattogram, the customer sees DG-100097 only; Chattogram's key gets 404 on OD-100155.

No errors in the app log. The same build passed CI on GitHub the same morning (281 + 6 + 138 tests against a
throwaway SQL Server, and both Docker images).
