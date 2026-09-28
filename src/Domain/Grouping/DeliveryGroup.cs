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
/// <see cref="LocksAt"/>. The deadline is fixed when the group opens and never moves. Only one group per
/// customer and address can be <see cref="DeliveryGroupStatus.Open"/> (unique index in the database).
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

    /// <summary>When the first order arrived (UTC). Day 1 is this moment's date in the tenant's time zone.</summary>
    public DateTime OpenedOn { get; private set; }

    /// <summary>
    /// Midnight at the start of delivery day in the tenant's time zone, stored as UTC. An order before this
    /// moment joins the group; an order at or after it starts a new one.
    /// </summary>
    public DateTime LocksAt { get; private set; }

    /// <summary>When the group actually locked: at <see cref="LocksAt"/>, or earlier when the customer chose Ship now.</summary>
    public DateTime? LockedOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Opens the group that later orders to the same customer and address join until <see cref="LocksAt"/>.</summary>
    public static DeliveryGroup Open(NewDeliveryGroup spec)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(spec.JoinDays, 1, nameof(spec.JoinDays));

        return Create(spec, spec.JoinDays, DeliveryGroupStatus.Open);
    }

    /// <summary>
    /// A group for one order that does not wait (Deliver fast, or Don't hold): locked at once, so nothing else
    /// joins it and it never blocks the customer's open group, and delivered the next day in the tenant's time
    /// zone. The tenant's join days do not apply.
    /// </summary>
    public static DeliveryGroup OpenAlone(NewDeliveryGroup spec)
    {
        var group = Create(spec, 1, DeliveryGroupStatus.Locked);
        group.LockedOn = spec.OpenedOn;

        return group;
    }

    /// <summary>True while an order placed at <paramref name="now"/> (UTC) may join this group.</summary>
    public bool CanJoin(DateTime now)
    {
        return Status == DeliveryGroupStatus.Open && now < LocksAt;
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

        return true;
    }

    public bool CanMoveTo(DeliveryGroupStatus status)
    {
        return Transitions[Status].Contains(status);
    }

    /// <summary>
    /// Moves the group on. Locking an open group (at the deadline or by Ship now) records
    /// <see cref="LockedOn"/>; returning to Locked for a re-attempt keeps the first lock time.
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

        Status = status;

        return Result.Success();
    }

    private static DeliveryGroup Create(NewDeliveryGroup spec, int daysBeforeDelivery, DeliveryGroupStatus status)
    {
        if (spec.OpenedOn.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OpenedOn must be UTC.", nameof(spec));
        }

        var firstDay = TimeZoneInfo.ConvertTimeFromUtc(spec.OpenedOn, spec.TimeZone).Date;
        var deliveryDay = DateTime.SpecifyKind(firstDay.AddDays(daysBeforeDelivery), DateTimeKind.Unspecified);

        return new DeliveryGroup
        {
            CustomerId = spec.CustomerId,
            AddressId = spec.AddressId,
            HubId = spec.HubId,
            Status = status,
            OpenedOn = spec.OpenedOn,

            // GetUtcOffset never throws, even where a daylight-saving change skips midnight
            LocksAt = new DateTimeOffset(deliveryDay, spec.TimeZone.GetUtcOffset(deliveryDay)).UtcDateTime
        };
    }
}
