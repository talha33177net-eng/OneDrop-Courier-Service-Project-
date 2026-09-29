using Domain.Common;

namespace Domain.Grouping;

/// <summary>
/// Everything needed to open a group. <see cref="TimeZone"/> and <see cref="JoinDays"/> are the tenant's
/// settings: the days are counted in the tenant's time zone, not in UTC.
/// </summary>
public sealed record NewDeliveryGroup(
    long CustomerId,
    long AddressId,
    long HubId,
    DateTime OpenedOn,
    TimeZoneInfo TimeZone,
    int JoinDays);

/// <summary>
/// All of one customer's orders to one address that travel together. The group opens with the first order;
/// orders placed before <see cref="LocksAt"/> join it, and it is delivered on the day that starts at
/// <see cref="LocksAt"/>. The deadline is fixed when the group opens and never moves later; only Ship now brings
/// it forward. Only one group per customer and address can be <see cref="DeliveryGroupStatus.Open"/> (unique
/// index in the database). A next-day delivery (<see cref="Kind"/>) also takes orders after it has locked, while their
/// parcels can still reach the hub by its delivery day (<see cref="CanTake"/>).
/// Merchants never see a group: it would tell them where else the customer shopped.
/// </summary>
public class DeliveryGroup : TenantEntity
{
    private static readonly Dictionary<DeliveryGroupStatus, DeliveryGroupStatus[]> Transitions = new()
    {
        [DeliveryGroupStatus.Open] = [DeliveryGroupStatus.Locked, DeliveryGroupStatus.Cancelled],
        [DeliveryGroupStatus.Locked] = [DeliveryGroupStatus.Dispatched, DeliveryGroupStatus.Cancelled],

        // Back to Locked when the customer was not home and the group waits at the hub for the re-attempt
        [DeliveryGroupStatus.Dispatched] =
            [DeliveryGroupStatus.Delivered, DeliveryGroupStatus.Locked, DeliveryGroupStatus.Cancelled],
        [DeliveryGroupStatus.Delivered] = [],
        [DeliveryGroupStatus.Cancelled] = []
    };

    private DeliveryGroup()
    {
    }

    public long CustomerId { get; private set; }

    public long AddressId { get; private set; }

    /// <summary>The hub serving the address's zone, where the group's parcels are shelved.</summary>
    public long HubId { get; private set; }

    /// <summary>
    /// Public group number (DG-100001), assigned by the database sequence on insert. Null until saved, for the
    /// same reason as <c>Order.Number</c>.
    /// </summary>
    public string Number { get; private set; } = null!;

    public DeliveryGroupStatus Status { get; private set; }

    /// <summary>Whether the delivery waits for the join days, goes out the next day, or was brought forward by Ship now.</summary>
    public DeliveryGroupKind Kind { get; private set; }

    /// <summary>When the first order arrived (UTC). Day 1 is this moment's date in the tenant's time zone.</summary>
    public DateTime OpenedOn { get; private set; }

    /// <summary>
    /// Midnight at the start of delivery day in the tenant's time zone, stored as UTC. An order before this
    /// moment joins the group; an order at or after it starts a new one. Ship now moves it to the next day.
    /// </summary>
    public DateTime LocksAt { get; private set; }

    /// <summary>When the group actually locked: at <see cref="LocksAt"/>, or earlier when the customer chose Ship now.</summary>
    public DateTime? LockedOn { get; private set; }

    /// <summary>
    /// The group's shelf at its hub, from the first parcel scanned in there until a rider takes the group out.
    /// Unique per hub among the groups holding one (<c>UX_DeliveryGroup_Hub_Shelf</c>); printed as
    /// <see cref="ShelfCode"/>.
    /// </summary>
    public int? Shelf { get; private set; }

    /// <summary>True while the group waits at the hub (open or locked) and has no shelf yet.</summary>
    public bool NeedsShelf => Shelf is null && Status is DeliveryGroupStatus.Open or DeliveryGroupStatus.Locked;

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>The shelf's label, hub code and number: <c>MIR-07</c>.</summary>
    public static string ShelfCode(string hubCode, int shelf)
    {
        return $"{hubCode}-{shelf:D2}";
    }

    /// <summary>Opens the group that later orders to the same customer and address join until <see cref="LocksAt"/>.</summary>
    public static DeliveryGroup Open(NewDeliveryGroup spec)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(spec.JoinDays, 1, nameof(spec.JoinDays));

        return Create(spec, spec.JoinDays, DeliveryGroupStatus.Open, DeliveryGroupKind.Waiting);
    }

    /// <summary>
    /// A group for an order that does not wait (Deliver fast, or Don't hold): locked at once, so it never blocks the
    /// customer's open group, and delivered the next day in the tenant's time zone. The tenant's join days do not
    /// apply. Other orders can still join it (<see cref="CanTake"/>).
    /// </summary>
    public static DeliveryGroup OpenAlone(NewDeliveryGroup spec)
    {
        var group = Create(spec, 1, DeliveryGroupStatus.Locked, DeliveryGroupKind.NextDay);
        group.LockedOn = spec.OpenedOn;

        return group;
    }

    /// <summary>
    /// A delivery for orders a rider left behind (<see cref="DeliveryGroupKind.FollowUp"/>): locked at once and
    /// delivered the next day, like <see cref="OpenAlone"/>, with the customer paying only the extra-shop fee.
    /// </summary>
    public static DeliveryGroup FollowUp(NewDeliveryGroup spec)
    {
        var group = Create(spec, 1, DeliveryGroupStatus.Locked, DeliveryGroupKind.FollowUp);
        group.LockedOn = spec.OpenedOn;

        return group;
    }

    /// <summary>Ship now on a group that is locked, on its way, delivered or cancelled.</summary>
    public static Error NotOpenForShipNow => Error.Conflict(
        "deliveryGroup.shipNow.notOpen",
        "This delivery has already closed, so it cannot be sent early.");

    /// <summary>
    /// Ship now: the customer stops waiting for more shops. The group locks at <paramref name="now"/> and is
    /// delivered the next day in the tenant's time zone instead of on Day 3; when that brings the day forward
    /// (<see cref="ShipNowBringsForward"/>) it becomes <see cref="DeliveryGroupKind.ShippedNow"/> and the customer
    /// pays the fast difference. On the last day to join it moves nothing and stays a waiting delivery. A group
    /// already past its deadline is locked as due and keeps its delivery day. A group that is no longer open cannot
    /// be sent early.
    /// </summary>
    public Result ShipNow(DateTime now, TimeZoneInfo timeZone)
    {
        if (Status != DeliveryGroupStatus.Open)
        {
            return NotOpenForShipNow;
        }

        if (LockIfDue(now))
        {
            return Result.Success();
        }

        if (ShipNowBringsForward(now, timeZone))
        {
            Kind = DeliveryGroupKind.ShippedNow;
        }

        Status = DeliveryGroupStatus.Locked;
        LockedOn = now;

        // The next midnight after now; never later than the deadline, which is itself a midnight after now
        LocksAt = StartOfDay(now, timeZone, daysAhead: 1);
        Raise(new DeliveryGroupLocked(this));

        return Result.Success();
    }

    /// <summary>
    /// True when Ship now at <paramref name="now"/> would deliver an open group earlier than its delivery day.
    /// </summary>
    public bool ShipNowBringsForward(DateTime now, TimeZoneInfo timeZone)
    {
        return CanJoin(now) && StartOfDay(now, timeZone, daysAhead: 1) < LocksAt;
    }

    /// <summary>True while an order placed at <paramref name="now"/> (UTC) may join this open group.</summary>
    public bool CanJoin(DateTime now)
    {
        return Status == DeliveryGroupStatus.Open && now < LocksAt;
    }

    /// <summary>
    /// True when a new order placed at <paramref name="now"/>, collected by the pickup run leaving at
    /// <paramref name="pickup"/> (UTC; null when its zone has no route), can travel in this delivery. An open group
    /// takes it before its deadline. A next-day delivery that has locked but not left the hub takes it while the
    /// pickup falls on or before its delivery day, so the parcels reach the hub before the riders leave. An order
    /// that must not wait (<paramref name="waits"/> false: Deliver fast, Don't hold) only joins a delivery that
    /// arrives by the next day.
    /// </summary>
    public bool CanTake(DateTime now, DateTime? pickup, bool waits, TimeZoneInfo timeZone)
    {
        var inTime = waits || LocksAt <= StartOfDay(now, timeZone, daysAhead: 1);
        if (CanJoin(now))
        {
            return inTime;
        }

        return Status == DeliveryGroupStatus.Locked &&
            Kind != DeliveryGroupKind.Waiting &&
            pickup is { } run &&
            StartOfDay(run, timeZone, daysAhead: 0) <= LocksAt &&
            inTime;
    }

    /// <summary>
    /// Locks an open group whose deadline has passed, recording <see cref="LocksAt"/> as the lock time however
    /// late this runs. False when the group is not open or not yet due.
    /// </summary>
    public bool LockIfDue(DateTime now)
    {
        if (Status != DeliveryGroupStatus.Open || now < LocksAt)
        {
            return false;
        }

        Status = DeliveryGroupStatus.Locked;
        LockedOn = LocksAt;
        Raise(new DeliveryGroupLocked(this));

        return true;
    }

    public bool CanMoveTo(DeliveryGroupStatus status)
    {
        return Transitions[Status].Contains(status);
    }

    /// <summary>
    /// Moves the group on. Locking records <see cref="LockedOn"/>; returning to Locked for a re-attempt keeps the
    /// first lock time. An open group is closed with <see cref="ShipNow"/> or <see cref="LockIfDue"/>, which also
    /// set the delivery day.
    /// </summary>
    public Result MoveTo(DeliveryGroupStatus status, DateTime now)
    {
        if (!CanMoveTo(status))
        {
            return Error.Conflict(
                "deliveryGroup.status.transition",
                $"A delivery group that is {Status} cannot become {status}.");
        }

        if (status == DeliveryGroupStatus.Locked)
        {
            LockedOn ??= now;
        }

        // A group leaving the hub frees its shelf; one back for a re-attempt gets a shelf at its next scan-in
        if (status is DeliveryGroupStatus.Dispatched or DeliveryGroupStatus.Delivered or DeliveryGroupStatus.Cancelled)
        {
            Shelf = null;
        }

        Status = status;

        return Result.Success();
    }

    /// <summary>Gives a group waiting at its hub the shelf the hub chose. A group keeps the shelf it has.</summary>
    public void PutOnShelf(int shelf)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shelf, 1);
        if (!NeedsShelf)
        {
            throw new InvalidOperationException($"Delivery {Number} is {Status} with shelf {Shelf}; it takes no new shelf.");
        }

        Shelf = shelf;
    }

    private static DeliveryGroup Create(
        NewDeliveryGroup spec,
        int daysBeforeDelivery,
        DeliveryGroupStatus status,
        DeliveryGroupKind kind)
    {
        if (spec.OpenedOn.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OpenedOn must be UTC.", nameof(spec));
        }

        return new DeliveryGroup
        {
            CustomerId = spec.CustomerId,
            AddressId = spec.AddressId,
            HubId = spec.HubId,
            Status = status,
            Kind = kind,
            OpenedOn = spec.OpenedOn,
            LocksAt = StartOfDay(spec.OpenedOn, spec.TimeZone, daysBeforeDelivery)
        };
    }

    /// <summary>Midnight <paramref name="daysAhead"/> days after the date of <paramref name="utc"/> in the time zone, as UTC.</summary>
    private static DateTime StartOfDay(DateTime utc, TimeZoneInfo timeZone, int daysAhead)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone).Date;
        var day = DateTime.SpecifyKind(today.AddDays(daysAhead), DateTimeKind.Unspecified);

        // GetUtcOffset never throws, even where a daylight-saving change skips midnight
        return new DateTimeOffset(day, timeZone.GetUtcOffset(day)).UtcDateTime;
    }
}
