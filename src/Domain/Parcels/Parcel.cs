using Domain.Common;
using Domain.Pricing;

namespace Domain.Parcels;

/// <summary>Who receives a parcel and what it is. The area, hubs and charges are resolved by the caller.</summary>
public sealed record ParcelDetails(
    long AreaId,
    long DeliveryHubId,
    string RecipientName,
    PhoneNumber RecipientPhone,
    string RecipientAddress,
    decimal CodAmount,
    int WeightGrams,
    string? ItemDescription,
    string? Note,
    ParcelCharges Charges);

/// <summary>Everything needed to book a parcel.</summary>
public sealed record NewParcel(long MerchantId, long PickupPointId, long PickupHubId, ParcelDetails Details)
{
    /// <summary>The merchant's own order or invoice number.</summary>
    public string? MerchantReference { get; init; }

    public string? IdempotencyKey { get; init; }

    public byte[]? RequestHash { get; init; }
}

/// <summary>
/// One consignment from a merchant to a recipient. It is booked <see cref="ParcelStatus.Pending"/>, collected from the
/// merchant, sorted at hubs, taken out by a rider and delivered, held for another day, or returned. Where it is lives
/// in three fields: <see cref="CurrentHubId"/> while at a hub, <see cref="TransferToHubId"/> while travelling between
/// hubs and <see cref="RiderId"/> while a rider has it; at most one is set. Its charges are worked out from the rate
/// card when it is booked and never change after it is picked up.
/// </summary>
public class Parcel : TenantEntity, IMerchantOwned
{
    public const int MaxWeightGrams = 30_000;
    public const decimal MaxCodAmount = 1_000_000m;

    private readonly List<ParcelEvent> events = [];

    private Parcel()
    {
    }

    public long MerchantId { get; private set; }

    public long PickupPointId { get; private set; }

    /// <summary>The hub that collects the parcel: its pickup point's zone's hub. A return is handed back from here.</summary>
    public long PickupHubId { get; private set; }

    /// <summary>The recipient's area.</summary>
    public long AreaId { get; private set; }

    /// <summary>The hub that delivers the parcel: the recipient's area's zone's hub.</summary>
    public long DeliveryHubId { get; private set; }

    /// <summary>
    /// The public tracking code (OD10000001), assigned by the database sequence on insert. Null until saved: a non-null
    /// value here would be sent to the database instead of letting the sequence fill it.
    /// </summary>
    public string TrackingCode { get; private set; } = null!;

    /// <summary>The merchant's own order or invoice number.</summary>
    public string? MerchantReference { get; private set; }

    /// <summary>The Idempotency-Key header. A retried request with the same key returns this parcel.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Hash of the request body, so a reused key with a different body is rejected.</summary>
    public byte[]? RequestHash { get; private set; }

    public string RecipientName { get; private set; } = "";

    /// <summary>E.164.</summary>
    public string RecipientPhone { get; private set; } = "";

    public string RecipientAddress { get; private set; } = "";

    public string? ItemDescription { get; private set; }

    public int WeightGrams { get; private set; }

    /// <summary>Cash the rider collects for the merchant. Zero when the recipient has already paid.</summary>
    public decimal CodAmount { get; private set; }

    /// <summary>Instructions for the rider, such as "call before coming".</summary>
    public string? Note { get; private set; }

    public ServiceArea ServiceArea { get; private set; }

    public decimal DeliveryCharge { get; private set; }

    public decimal CodChargePercent { get; private set; }

    public decimal ReturnCharge { get; private set; }

    public ParcelStatus Status { get; private set; }

    public long? CurrentHubId { get; private set; }

    public long? TransferToHubId { get; private set; }

    public long? RiderId { get; private set; }

    /// <summary>Delivery attempts made: each time a rider recorded what happened at the door.</summary>
    public int Attempts { get; private set; }

    public string? HoldReason { get; private set; }

    /// <summary>The day the recipient asked to be delivered on, when they gave one.</summary>
    public DateOnly? HoldUntil { get; private set; }

    /// <summary>Why the parcel is going back to the merchant.</summary>
    public string? ReturnReason { get; private set; }

    /// <summary>The cash collected at the door; set once delivered.</summary>
    public decimal? CollectedAmount { get; private set; }

    /// <summary>The COD charge on <see cref="CollectedAmount"/>; set once delivered.</summary>
    public decimal? CodCharge { get; private set; }

    /// <summary>When the parcel reached a final status: delivered, returned or cancelled (UTC).</summary>
    public DateTime? ClosedOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<ParcelEvent> Events => events;

    public ParcelCharges Charges => new(ServiceArea, DeliveryCharge, CodChargePercent, ReturnCharge);

    public bool IsFinal => Status.IsFinal();

    /// <summary>What the merchant pays for the parcel so far; final once the parcel is.</summary>
    public decimal TotalCharge => Status switch
    {
        ParcelStatus.Delivered or ParcelStatus.PartlyDelivered => DeliveryCharge + (CodCharge ?? 0),
        ParcelStatus.Returned => DeliveryCharge + ReturnCharge,
        ParcelStatus.Cancelled => 0,
        _ => DeliveryCharge + ParcelCharges.CodCharge(CodAmount, CodChargePercent)
    };

    public static Result<Parcel> Create(NewParcel spec)
    {
        var parcel = new Parcel
        {
            MerchantId = spec.MerchantId,
            PickupPointId = spec.PickupPointId,
            PickupHubId = spec.PickupHubId,
            MerchantReference = spec.MerchantReference.NullIfBlank(),
            IdempotencyKey = spec.IdempotencyKey.NullIfBlank(),
            RequestHash = spec.RequestHash,
            Status = ParcelStatus.Pending
        };
        if (parcel.MerchantReference?.Length > 100)
        {
            return Error.Validation("parcel.reference", "The invoice or order number is at most 100 characters.");
        }

        var applied = parcel.Apply(spec.Details);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        parcel.events.Add(new ParcelEvent(parcel, ParcelStatus.Pending, "Parcel booked", null));

        return parcel;
    }

    /// <summary>The merchant corrects the parcel before it is picked up; the caller prices it again.</summary>
    public Result Edit(ParcelDetails details)
    {
        if (Status != ParcelStatus.Pending)
        {
            return Error.Conflict("parcel.edit", $"{TrackingCode} has been picked up and can no longer be changed.");
        }

        var applied = Apply(details);
        if (applied.IsSuccess)
        {
            events.Add(new ParcelEvent(this, Status, "Parcel details changed by the merchant", null));
        }

        return applied;
    }

    /// <summary>The merchant calls the parcel off before it is picked up.</summary>
    public Result Cancel(string? reason, DateTime now)
    {
        if (Status != ParcelStatus.Pending)
        {
            return Error.Conflict(
                "parcel.cancel",
                $"{TrackingCode} has been picked up. Ask for it to be returned instead.");
        }

        ClosedOn = now;
        MoveTo(ParcelStatus.Cancelled, reason.NullIfBlank() is { } why ? $"Cancelled: {why}" : "Cancelled by the merchant");

        return Result.Success();
    }

    /// <summary>A rider collected the parcel from the merchant. Collecting it again changes nothing.</summary>
    public Result<ScanOutcome> PickUp()
    {
        if (Status == ParcelStatus.PickedUp)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        if (Status != ParcelStatus.Pending)
        {
            return Error.Conflict("parcel.pickup", $"{TrackingCode} is {Status.DisplayName().ToLowerInvariant()}, so there is nothing to collect.");
        }

        MoveTo(ParcelStatus.PickedUp, "Picked up from the merchant");

        return ScanOutcome.Recorded;
    }

    /// <summary>
    /// Hub staff scanned the parcel in at <paramref name="hubId"/>: from the pickup rider (or the merchant's own
    /// drop-off), off the transfer from another hub, or back from a rider who could not deliver it. Scanning it again at
    /// the same hub changes nothing.
    /// </summary>
    public Result<ScanOutcome> ReceiveAt(long hubId)
    {
        if (CurrentHubId == hubId)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        switch (Status)
        {
            case ParcelStatus.Pending or ParcelStatus.PickedUp:
                var dropped = Status == ParcelStatus.Pending;
                Place(hubId);
                MoveTo(ParcelStatus.AtHub, dropped ? "Dropped off at the hub by the merchant" : "Received at the hub", hubId);
                return ScanOutcome.Recorded;

            case ParcelStatus.InTransit:
                Place(hubId);
                MoveTo(ParcelStatus.AtHub, "Arrived at the hub", hubId);
                return ScanOutcome.Recorded;

            case ParcelStatus.OnHold or ParcelStatus.Returning when CurrentHubId is null:
                var fromRider = RiderId is not null;
                Place(hubId);
                events.Add(new ParcelEvent(this, Status, fromRider ? "Back at the hub from the rider" : "Arrived at the hub", hubId));
                return ScanOutcome.Recorded;

            case ParcelStatus.AtHub or ParcelStatus.OnHold or ParcelStatus.Returning:
                return Error.Conflict(
                    "parcel.scan.otherHub",
                    $"{TrackingCode} is recorded at another hub. Dispatch it from there before scanning it in here.");

            case ParcelStatus.OutForDelivery:
                return Error.Conflict(
                    "parcel.scan.withRider",
                    $"{TrackingCode} is out with a rider. The rider records what happened at the door first.");

            default:
                return Error.Conflict(
                    "parcel.scan.final",
                    $"{TrackingCode} is {Status.DisplayName().ToLowerInvariant()}; there is nothing more to scan.");
        }
    }

    /// <summary>
    /// Hub staff sent the parcel from <paramref name="fromHubId"/> to <paramref name="toHubId"/>: forward towards the hub
    /// that delivers it, or, when it is returning, back towards the hub that collected it. Sending it again to the same
    /// hub changes nothing.
    /// </summary>
    public Result<ScanOutcome> DispatchTo(long fromHubId, long toHubId)
    {
        if (TransferToHubId == toHubId && Status is ParcelStatus.InTransit or ParcelStatus.Returning)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        if (CurrentHubId != fromHubId || Status is not (ParcelStatus.AtHub or ParcelStatus.Returning))
        {
            return Error.Conflict(
                "parcel.dispatch.notHere",
                $"{TrackingCode} is not at this hub. Scan it in before sending it on.");
        }

        if (toHubId == fromHubId)
        {
            return Error.Validation("parcel.dispatch.sameHub", "Choose another hub to send the parcel to.");
        }

        if (Status == ParcelStatus.AtHub && DeliveryHubId == fromHubId)
        {
            return Error.Conflict(
                "parcel.dispatch.deliverHere",
                $"{TrackingCode} is delivered from this hub. Assign it to a rider instead.");
        }

        if (Status == ParcelStatus.Returning && PickupHubId == fromHubId)
        {
            return Error.Conflict(
                "parcel.dispatch.returnHere",
                $"{TrackingCode} goes back to its merchant from this hub. Hand it back instead.");
        }

        CurrentHubId = null;
        TransferToHubId = toHubId;
        if (Status == ParcelStatus.Returning)
        {
            events.Add(new ParcelEvent(this, Status, "Sent back towards the merchant's hub", fromHubId));
        }
        else
        {
            MoveTo(ParcelStatus.InTransit, "Sent to the delivery hub", fromHubId);
        }

        return ScanOutcome.Recorded;
    }

    /// <summary>
    /// The hub handed the parcel to rider <paramref name="riderId"/> to deliver. Only from the hub that delivers it, and
    /// only while it waits there for a rider (first time or after a hold).
    /// </summary>
    public Result AssignTo(long riderId, long hubId)
    {
        if (Status is not (ParcelStatus.AtHub or ParcelStatus.OnHold) || CurrentHubId != hubId)
        {
            return Error.Conflict(
                "parcel.assign.notHere",
                $"{TrackingCode} is not waiting at this hub for a rider.");
        }

        if (DeliveryHubId != hubId)
        {
            return Error.Conflict(
                "parcel.assign.otherHub",
                $"{TrackingCode} is delivered from another hub. Send it there first.");
        }

        CurrentHubId = null;
        RiderId = riderId;
        HoldReason = null;
        HoldUntil = null;
        MoveTo(ParcelStatus.OutForDelivery, "Out for delivery with the rider", hubId);

        return Result.Success();
    }

    /// <summary>
    /// The rider handed the parcel over and collected <paramref name="collected"/>: the whole cash on delivery
    /// (delivered), or less when the recipient kept only part of it (partly delivered, with the reason).
    /// </summary>
    public Result Deliver(decimal collected, string? reason, DateTime now)
    {
        if (Status != ParcelStatus.OutForDelivery)
        {
            return NotOutForDelivery();
        }

        if (collected < 0 || collected > CodAmount)
        {
            return Error.Validation(
                "parcel.deliver.amount",
                $"The amount collected must be between ৳0 and the cash on delivery, ৳{CodAmount:N0}.");
        }

        var partly = collected < CodAmount;
        var why = reason.NullIfBlank();
        if (partly && why is null)
        {
            return Error.Validation(
                "parcel.deliver.reason",
                "Say why less than the cash on delivery was collected, such as \"kept 2 of 3 items\".");
        }

        Attempts++;
        CollectedAmount = collected;
        CodCharge = ParcelCharges.CodCharge(collected, CodChargePercent);
        RiderId = null;
        ClosedOn = now;
        MoveTo(
            partly ? ParcelStatus.PartlyDelivered : ParcelStatus.Delivered,
            partly ? $"Partly delivered, ৳{collected:N0} collected: {why}" : $"Delivered, ৳{collected:N0} collected");

        return Result.Success();
    }

    /// <summary>
    /// The recipient could not take the parcel today (not reachable, asked for another day). The rider brings it back to
    /// the hub for another attempt; after <paramref name="maxAttempts"/> attempts it must be delivered or returned.
    /// </summary>
    public Result Hold(string? reason, DateOnly? until, int maxAttempts)
    {
        if (Status != ParcelStatus.OutForDelivery)
        {
            return NotOutForDelivery();
        }

        var why = reason.NullIfBlank();
        if (why is null || why.Length > 200)
        {
            return Error.Validation("parcel.hold.reason", "Say why the parcel could not be delivered, at most 200 characters.");
        }

        if (Attempts + 1 >= maxAttempts)
        {
            return Error.Conflict(
                "parcel.hold.attempts",
                $"This is attempt {Attempts + 1} of {maxAttempts}: deliver the parcel or return it to the merchant.");
        }

        Attempts++;
        HoldReason = why;
        HoldUntil = until;
        MoveTo(ParcelStatus.OnHold, until is null ? $"On hold: {why}" : $"On hold until {until:ddd d MMM}: {why}");

        return Result.Success();
    }

    /// <summary>The recipient refused the parcel at the door: it goes back to the merchant.</summary>
    public Result Refuse(string? reason)
    {
        if (Status != ParcelStatus.OutForDelivery)
        {
            return NotOutForDelivery();
        }

        var why = reason.NullIfBlank();
        if (why is null || why.Length > 200)
        {
            return Error.Validation("parcel.refuse.reason", "Say why the recipient did not take the parcel, at most 200 characters.");
        }

        Attempts++;
        ReturnReason = why;
        MoveTo(ParcelStatus.Returning, $"Returning to the merchant: {why}");

        return Result.Success();
    }

    /// <summary>
    /// The merchant or the hub asks for a parcel not yet out with a rider to come back. It returns from wherever it is.
    /// </summary>
    public Result RequestReturn(string? reason)
    {
        if (Status is not (ParcelStatus.PickedUp or ParcelStatus.AtHub or ParcelStatus.InTransit or ParcelStatus.OnHold))
        {
            return Error.Conflict(
                "parcel.return",
                Status == ParcelStatus.Pending
                    ? $"{TrackingCode} has not been picked up yet. Cancel it instead."
                    : $"{TrackingCode} is {Status.DisplayName().ToLowerInvariant()} and cannot be returned now.");
        }

        var why = reason.NullIfBlank() ?? "Return requested";
        if (why.Length > 200)
        {
            return Error.Validation("parcel.return.reason", "Say why in at most 200 characters.");
        }

        ReturnReason = why;
        MoveTo(ParcelStatus.Returning, $"Returning to the merchant: {why}", CurrentHubId);

        return Result.Success();
    }

    /// <summary>The hub that collected a returning parcel handed it back to the merchant. Final.</summary>
    public Result<ScanOutcome> ReturnToMerchant(long hubId, DateTime now)
    {
        if (Status == ParcelStatus.Returned)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        if (Status != ParcelStatus.Returning)
        {
            return Error.Conflict(
                "parcel.handBack",
                $"{TrackingCode} is {Status.DisplayName().ToLowerInvariant()}; only a returning parcel goes back to the merchant.");
        }

        if (CurrentHubId != hubId)
        {
            return Error.Conflict("parcel.handBack.notHere", $"{TrackingCode} is not at this hub. Scan it in first.");
        }

        if (PickupHubId != hubId)
        {
            return Error.Conflict(
                "parcel.handBack.otherHub",
                $"{TrackingCode} goes back to its merchant from another hub. Send it there first.");
        }

        CurrentHubId = null;
        ClosedOn = now;
        MoveTo(ParcelStatus.Returned, "Handed back to the merchant", hubId);

        return ScanOutcome.Recorded;
    }

    private Result Apply(ParcelDetails details)
    {
        var name = details.RecipientName.NullIfBlank();
        if (name is null || name.Length > 200)
        {
            return Error.Validation("parcel.recipient.name", "Enter the recipient's name, at most 200 characters.");
        }

        var address = details.RecipientAddress.NullIfBlank();
        if (address is null || address.Length > 500)
        {
            return Error.Validation("parcel.recipient.address", "Enter the delivery address, at most 500 characters.");
        }

        if (details.WeightGrams is < 1 or > MaxWeightGrams)
        {
            return Error.Validation("parcel.weight", $"The weight must be between 1 g and {MaxWeightGrams / 1000} kg.");
        }

        if (details.CodAmount is < 0 or > MaxCodAmount)
        {
            return Error.Validation("parcel.cod", $"The cash on delivery must be between ৳0 and ৳{MaxCodAmount:N0}.");
        }

        if (details.ItemDescription?.Trim().Length > 200 || details.Note?.Trim().Length > 500)
        {
            return Error.Validation("parcel.text", "The item description is at most 200 characters and the note 500.");
        }

        AreaId = details.AreaId;
        DeliveryHubId = details.DeliveryHubId;
        RecipientName = name;
        RecipientPhone = details.RecipientPhone.Value;
        RecipientAddress = address;
        CodAmount = details.CodAmount;
        WeightGrams = details.WeightGrams;
        ItemDescription = details.ItemDescription.NullIfBlank();
        Note = details.Note.NullIfBlank();
        ServiceArea = details.Charges.ServiceArea;
        DeliveryCharge = details.Charges.DeliveryCharge;
        CodChargePercent = details.Charges.CodChargePercent;
        ReturnCharge = details.Charges.ReturnCharge;

        return Result.Success();
    }

    private void Place(long hubId)
    {
        CurrentHubId = hubId;
        TransferToHubId = null;
        RiderId = null;
    }

    private void MoveTo(ParcelStatus status, string note, long? hubId = null)
    {
        Status = status;
        events.Add(new ParcelEvent(this, status, note, hubId));
        Raise(new ParcelStatusChanged(this, status));
    }

    private Error NotOutForDelivery()
    {
        return Error.Conflict(
            "parcel.notOut",
            $"{TrackingCode} is {Status.DisplayName().ToLowerInvariant()}, not out for delivery.");
    }
}
