using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Network;
using Domain.Parcels;
using Domain.Payments;

namespace Application.Hubs.HubScan;

/// <summary>What a hub scan can do.</summary>
public enum ScanMode
{
    /// <summary>A parcel arrives: from a pickup, off a transfer, or back from a rider.</summary>
    Receive,

    /// <summary>A parcel leaves for another hub: forward to the hub that delivers it, or back to the hub that collected it.</summary>
    Dispatch,

    /// <summary>A returning parcel is handed back to its merchant.</summary>
    HandBack
}

public enum ScanTone
{
    Success,
    Info,
    Error
}

/// <summary>The large answer the scan page shows: what happened and what to do with the parcel next.</summary>
public sealed record ScanAnswer(ScanTone Tone, string Title, string? Detail = null, string? TrackingCode = null);

/// <summary>
/// Hub staff scan a parcel's label at their hub. Each scan says plainly what to do next: assign it to a rider, send it to
/// another hub, or hand it back to the merchant. Scanning the same parcel twice is harmless. Another courier's parcel or
/// hub is not found. Two scans of one parcel at once lose on its row version and are retried from fresh data.
/// </summary>
public class HubScanHandler(IAppDbContext db, ITenantContext tenantContext, HubDirectory hubs, TimeProvider time)
{
    private const int Retries = 3;

    public static readonly Error UnknownHub = Error.NotFound("hub.notFound", "That hub was not found.");

    public async Task<Result<ScanAnswer>> ScanAsync(
        string hubCode,
        ScanMode mode,
        string? trackingCode,
        CancellationToken cancellationToken = default)
    {
        var hub = await hubs.FindAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return UnknownHub;
        }

        var code = trackingCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            return new ScanAnswer(ScanTone.Error, "Scan or type a tracking code.");
        }

        for (var attempt = 1; ; attempt++)
        {
            var parcel = await db.Parcels.SingleOrDefaultAsync(p => p.TrackingCode == code, cancellationToken);
            if (parcel is null)
            {
                return new ScanAnswer(ScanTone.Error, $"{code} was not found.", "Check the label and scan it again.", code);
            }

            var answer = mode switch
            {
                ScanMode.Receive => await ReceiveAsync(parcel, hub, cancellationToken),
                ScanMode.Dispatch => await DispatchAsync(parcel, hub, cancellationToken),
                _ => await HandBackAsync(parcel, hub, cancellationToken)
            };

            try
            {
                await db.SaveChangesAsync(cancellationToken);

                return answer;
            }
            catch (DbUpdateConcurrencyException) when (attempt < Retries)
            {
                // Someone else moved the parcel meanwhile: forget this attempt and scan again from fresh rows
                foreach (var entry in db.Parcels.Local.Cast<object>()
                    .Concat(db.ParcelEvents.Local)
                    .Concat(db.LedgerEntries.Local)
                    .ToList())
                {
                    db.Entry(entry).State = EntityState.Detached;
                }
            }
        }
    }

    private async Task<ScanAnswer> ReceiveAsync(Parcel parcel, Hub hub, CancellationToken cancellationToken)
    {
        var received = parcel.ReceiveAt(hub.Id);
        if (received.IsFailure)
        {
            return new ScanAnswer(ScanTone.Error, received.Error!.Message, null, parcel.TrackingCode);
        }

        var title = received.Value == ScanOutcome.AlreadyRecorded ? "Already scanned in here" : "Received";
        var next = await NextStepAsync(parcel, hub, cancellationToken);

        return new ScanAnswer(received.Value == ScanOutcome.AlreadyRecorded ? ScanTone.Info : ScanTone.Success, title, next, parcel.TrackingCode);
    }

    private async Task<ScanAnswer> DispatchAsync(Parcel parcel, Hub hub, CancellationToken cancellationToken)
    {
        var toHubId = parcel.Status == ParcelStatus.Returning ? parcel.PickupHubId : parcel.DeliveryHubId;
        var dispatched = parcel.DispatchTo(hub.Id, toHubId);
        if (dispatched.IsFailure)
        {
            return new ScanAnswer(ScanTone.Error, dispatched.Error!.Message, null, parcel.TrackingCode);
        }

        var to = await db.Hubs.Where(h => h.Id == toHubId).Select(h => h.Name).SingleAsync(cancellationToken);

        return dispatched.Value == ScanOutcome.AlreadyRecorded
            ? new ScanAnswer(ScanTone.Info, $"Already on its way to {to}", null, parcel.TrackingCode)
            : new ScanAnswer(ScanTone.Success, $"Send to {to}", "Put it in the bag for that hub.", parcel.TrackingCode);
    }

    private async Task<ScanAnswer> HandBackAsync(Parcel parcel, Hub hub, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Require();
        var now = time.GetUtcNow().UtcDateTime;
        var returned = parcel.ReturnToMerchant(hub.Id, now);
        if (returned.IsFailure)
        {
            return new ScanAnswer(ScanTone.Error, returned.Error!.Message, null, parcel.TrackingCode);
        }

        var merchant = await db.Merchants.Where(m => m.Id == parcel.MerchantId).Select(m => m.Name).SingleAsync(cancellationToken);
        if (returned.Value == ScanOutcome.AlreadyRecorded)
        {
            return new ScanAnswer(ScanTone.Info, $"Already returned to {merchant}", null, parcel.TrackingCode);
        }

        db.LedgerEntries.AddRange(LedgerEntry.For(parcel, tenant.Today(now)));

        return new ScanAnswer(
            ScanTone.Success,
            $"Returned to {merchant}",
            $"Get the merchant's signature. Charged ৳{parcel.TotalCharge:N0} (delivery and return).",
            parcel.TrackingCode);
    }

    /// <summary>What hub staff do with a parcel that is now at their hub.</summary>
    private async Task<string> NextStepAsync(Parcel parcel, Hub hub, CancellationToken cancellationToken)
    {
        async Task<string> HubName(long id)
        {
            return await db.Hubs.Where(h => h.Id == id).Select(h => h.Name).SingleAsync(cancellationToken);
        }

        switch (parcel.Status)
        {
            case ParcelStatus.Returning when parcel.PickupHubId == hub.Id:
                var merchant = await db.Merchants.Where(m => m.Id == parcel.MerchantId).Select(m => m.Name).SingleAsync(cancellationToken);
                return $"Returning: hand it back to {merchant}.";

            case ParcelStatus.Returning:
                return $"Returning: send it to {await HubName(parcel.PickupHubId)}.";

            case ParcelStatus.OnHold:
                return $"On hold ({parcel.HoldReason})" +
                    (parcel.HoldUntil is { } until ? $" until {until:ddd d MMM}" : "") +
                    ". Assign it to a rider again.";

            case ParcelStatus.AtHub when parcel.DeliveryHubId == hub.Id:
                var area = await db.Areas.Where(a => a.Id == parcel.AreaId).Select(a => a.Name).SingleAsync(cancellationToken);
                return $"Delivered from this hub ({area}): assign it to a rider.";

            default:
                return $"Send it to {await HubName(parcel.DeliveryHubId)}.";
        }
    }
}
