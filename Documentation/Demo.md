# Demo script

A presenter's script for showing OneDrop Courier end to end in about fifteen minutes, from a merchant booking a parcel
to the merchant being paid. Every step is a screen; nothing needs SQL.

**Before you start:** `./tools/db/publish.ps1`, then `dotnet run --project src/Web`. In Development the app seeds
the demo logins (password `OneDrop#2026`, one-press buttons on the sign-in page) and about twenty sample parcels in
every stage, so the dashboards are not empty. Open http://onedrop.localhost:5080. Keep a second browser window
(or a private window) for the other roles.

| # | Who | Where | Show |
|---|---|---|---|
| 1 | Visitor | `/` | The public site: services, the rate card (Inside city ৳60, Suburb ৳100, Outside city ৳120), tracking box, "Become a merchant" |
| 2 | New shop | `/Account/Register` | Sign up a shop with a pickup address; it lands on its dashboard "waiting for approval" and cannot book yet |
| 3 | Courier admin `admin@onedrop.test` | `/Admin` | The live dashboard; the dark "merchants waiting for approval" banner → **Approve** on the merchant's page |
| 4 | Merchant `fashion@onedrop.test` | **Book a parcel** | Pick an area: the charges appear as you type (service area, delivery charge, COD charge). Book with ৳1,250 COD to Gulshan 2 |
| 5 | Merchant | **Fraud check** | Type the recipient's number: delivered and returned counts at every merchant, no merchant named |
| 6 | Merchant | **Bulk upload** | Download the template, upload it: every row booked, or nothing and the rows to fix |
| 7 | Merchant | **Print labels**, **Pickup requests** | QR labels with the hub code; ask for a pickup today |
| 8 | Hub staff `hub@onedrop.test` | `/Hub/Pickups?hub=MIR` | Assign the pickup to Rafiq Hasan |
| 9 | Rider `rider@onedrop.test` | `/Rider/Pickups` (phone width) | Tick the parcels and **Picked up** |
| 10 | Hub staff | **Scan parcels** (Mirpur) | Receive the parcel ("Received … Send it to Gulshan hub"), then the Dispatch tab |
| 11 | Hub staff | **Scan parcels** (Gulshan) | Receive it at Gulshan: "Delivered from this hub (Gulshan 2): assign it to a rider" |
| 12 | Hub staff | **Assign to riders** (Gulshan) | Tick it, choose Kamal Uddin |
| 13 | Anyone | **SMS inbox** (demo bar) | "Your parcel OD… is out for delivery today with Kamal Uddin … Please keep ৳1,250 ready" |
| 14 | Rider `rider3@onedrop.test` | `/Rider` | The stop with what to collect → **Record outcome** → Delivered, ৳1,250 |
| 15 | Visitor | `/Track?code=OD…` | The parcel's way, hub by hub; no address or amount shown |
| 16 | Hub staff | **Rider closing** (Gulshan) | Close Kamal's run: received ৳1,200 → "short ৳50" on the run |
| 17 | Merchant | **Payments** | The line waiting for the next payout: ৳1,250 less ৳60 and ৳13 |
| 18 | Courier admin | **Merchant payouts** → **Run payouts now** | Pays every merchant its lines up to yesterday; today's wait for tomorrow. The next morning (the hourly job does it by itself) the payout appears with its INV- number; **Payouts sent** in the demo bar shows the transfer |
| 19 | Courier admin | **Delivery rates**, **Riders**, **Coverage** | Change a rate (new bookings only), add a rider with a login, the hubs and areas served |
| 20 | Merchant `gadget@onedrop.test` | `/Merchant/Parcel/OD…` (Fashion House's code) | 404: a merchant never sees another's parcels |

## Also worth showing

- **Refused at the door.** Book to Savar from Mirpur; at the door choose **Refused**: the parcel goes back through the
  hubs as Returning, and the Mirpur hub hands it back (**Scan parcels**, Hand back tab). The merchant pays the delivery
  charge and the suburb's return charge.
- **On hold.** "Customer not reachable" with a new date: back to the hub at run closing, out again on the date; the
  third failed attempt sends it back.
- **API.** The `curl` in the README books a parcel; set the merchant's webhook to
  `http://localhost:5080/Dev/Webhooks` and watch each status change arrive signed.
- **Simulator.** `dotnet run --project tools/Simulator -- --parcels 40 --pace 2` while `/Admin` is open: the counts
  move without a reload.
