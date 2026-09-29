using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Grouping;
using Domain.Network;
using Domain.Orders;

namespace Application.Network.HubScan;

/// <summary>A hub hub staff can work at.</summary>
public sealed record HubChoice(string Code, string Name);

/// <summary>
/// What the scanner shows after a label. <see cref="Shelf"/> is set when the parcel reached the hub its delivery
/// leaves from; <see cref="SendTo"/> names that hub when it reached another one (the shuttle takes it on).
/// <see cref="PackagesIn"/> counts the order's packages that have reached a hub, including any on the shuttle.
/// </summary>
public sealed record ParcelScan(
    string Label,
    string Order,
    string Merchant,
    int Packages,
    int PackagesIn,
    OrderStatus Status,
    bool AlreadyScanned,
    bool Urgent,
    string Delivery,
    DeliveryGroupStatus DeliveryStatus,
    DateOnly DeliveryDay,
    string? Shelf,
    string? SendTo);

/// <summary>
/// A delivery on a shelf of the hub, with how many of its parcels are there. <see cref="Ready"/> once it is closed
/// and every parcel still for delivery is on the shelf.
/// </summary>
public sealed record ShelfRow(
    string Shelf,
    string Delivery,
    DeliveryGroupStatus Status,
    DateOnly DeliveryDay,
    int Orders,
    int PackagesHere,
    int Packages)
{
    public bool Ready => Status == DeliveryGroupStatus.Locked && PackagesHere == Packages;
}

/// <summary>
/// A parcel on the shuttle manifest. <see cref="Urgent"/> parcels travel in a next-day delivery (Deliver fast,
/// Don't hold, Ship now, or an order that joined one) and must not miss the next run.
/// </summary>
public sealed record ShuttleParcel(string Label, string Merchant, string Delivery, DateOnly DeliveryDay, bool Urgent);

/// <summary>The parcels to load for one hub.</summary>
public sealed record ShuttleLoad(string Hub, string HubName, IReadOnlyList<ShuttleParcel> Parcels);

/// <summary>What a hub sends on the shuttle, by destination, and what is on its way to it.</summary>
public sealed record ShuttleManifest(IReadOnlyList<ShuttleLoad> ToLoad, IReadOnlyList<ShuttleParcel> OnTheWay);

/// <summary>
/// Parcel scans by the operator's staff. At the shop a scan collects the order (<see cref="Order.Collect"/>); at the
/// hub it receives the package (<see cref="Order.ReceiveAtHub"/>) and, at the hub the delivery leaves from, puts the
/// delivery on a shelf: the lowest free number at that hub, kept until a rider takes it out. A parcel that reached
/// another hub is loaded on the hub shuttle (<see cref="Order.LoadForShuttle"/>) and received again at the other end.
/// A label from another operator is not found, as the query filter hides its order. Two scans at the same moment
/// (two packages of one order, or two deliveries taking the same free shelf) are settled by the row version and the
/// shelf's unique index: the loser scans again.
/// </summary>
public class HubScanHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    private const int MaxAttempts = 5;

    public async Task<IReadOnlyList<HubChoice>> HubsAsync(CancellationToken cancellationToken = default)
    {
        return await db.Hubs
            .Where(hub => !hub.Archived)
            .OrderBy(hub => hub.Name)
            .Select(hub => new HubChoice(hub.Code, hub.Name))
            .ToListAsync(cancellationToken);
    }

    public Task<Result<ParcelScan>> CollectAsync(string? label, CancellationToken cancellationToken = default)
    {
        return ScanAsync(label, hubId: null, (order, _) => Task.FromResult(order.Collect()), cancellationToken);
    }

    public async Task<Result<ParcelScan>> ReceiveAsync(
        string hubCode,
        string? label,
        CancellationToken cancellationToken = default)
    {
        var hub = await FindHubAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return Error.NotFound("hubScan.hub.unknown", $"There is no hub {hubCode}.");
        }

        return await ScanAsync(
            label,
            hub.Id,
            async (order, sequence) =>
            {
                var received = order.ReceiveAtHub(sequence, hub.Id, time.GetUtcNow().UtcDateTime);
                var group = order.DeliveryGroup!;
                if (received.IsSuccess && group.HubId == hub.Id && group.NeedsShelf)
                {
                    var taken = (await db.DeliveryGroups
                        .Where(g => g.HubId == hub.Id && g.Shelf != null)
                        .Select(g => g.Shelf!.Value)
                        .ToListAsync(cancellationToken)).ToHashSet();
                    var shelf = 1;
                    while (taken.Contains(shelf))
                    {
                        shelf++;
                    }

                    group.PutOnShelf(shelf);
                }

                return received;
            },
            cancellationToken);
    }

    /// <summary>Loads a parcel scanned in here on the shuttle to the hub its delivery leaves from.</summary>
    public async Task<Result<ParcelScan>> LoadAsync(
        string hubCode,
        string? label,
        CancellationToken cancellationToken = default)
    {
        var hub = await FindHubAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return Error.NotFound("hubScan.hub.unknown", $"There is no hub {hubCode}.");
        }

        return await ScanAsync(
            label,
            hub.Id,
            (order, sequence) => Task.FromResult(order.LoadForShuttle(sequence, hub.Id, order.DeliveryGroup!.HubId)),
            cancellationToken);
    }

    /// <summary>
    /// The hub's shuttle manifest: the parcels here to load, by the hub they go to, and the parcels on their way
    /// here. Parcels of cancelled, refused or returned orders do not travel. Parcels for the soonest delivery day
    /// (today's trips) come first, next-day deliveries first within a day. Null for a hub that is not this operator's.
    /// </summary>
    public async Task<ShuttleManifest?> ShuttleAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var hub = await FindHubAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return null;
        }

        var rows = await (
            from package in db.Packages
            join order in db.Orders on package.OrderId equals order.Id
            join g in db.DeliveryGroups on order.DeliveryGroupId equals g.Id
            join to in db.Hubs on g.HubId equals to.Id
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            where (package.HubId == hub.Id && g.HubId != hub.Id) || package.ShuttleToHubId == hub.Id
            select new
            {
                order.Number,
                package.Sequence,
                order.Status,
                Merchant = merchant.Name,
                Delivery = g.Number,
                g.LocksAt,
                Urgent = g.Kind != DeliveryGroupKind.Waiting,
                OnTheWay = package.ShuttleToHubId == hub.Id,
                ToHub = to.Code,
                ToHubName = to.Name
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var timeZone = TenantTimeZone();
        var parcels = rows
            .Where(row => Order.IsForDelivery(row.Status))
            .OrderBy(row => row.LocksAt)
            .ThenByDescending(row => row.Urgent)
            .ThenBy(row => row.Number)
            .ThenBy(row => row.Sequence)
            .Select(row => (
                row.OnTheWay,
                row.ToHub,
                row.ToHubName,
                Parcel: new ShuttleParcel(
                    new PackageLabel(row.Number, row.Sequence).ToString(),
                    row.Merchant,
                    row.Delivery,
                    LocalDay(row.LocksAt, timeZone),
                    row.Urgent)))
            .ToList();

        return new ShuttleManifest(
            [
                .. parcels
                    .Where(row => !row.OnTheWay)
                    .GroupBy(row => (row.ToHub, row.ToHubName))
                    .OrderBy(load => load.Key.ToHubName)
                    .Select(load => new ShuttleLoad(
                        load.Key.ToHub,
                        load.Key.ToHubName,
                        [.. load.Select(row => row.Parcel)]))
            ],
            [.. parcels.Where(row => row.OnTheWay).Select(row => row.Parcel)]);
    }

    /// <summary>The deliveries on the hub's shelves, by shelf. Null for a hub that is not this operator's.</summary>
    public async Task<IReadOnlyList<ShelfRow>?> ShelvesAsync(
        string hubCode,
        CancellationToken cancellationToken = default)
    {
        var hub = await FindHubAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return null;
        }

        var groups = await db.DeliveryGroups
            .Where(g => g.HubId == hub.Id && g.Shelf != null)
            .OrderBy(g => g.Shelf)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var ids = groups.Select(g => g.Id).ToList();
        var parcels = await (
            from package in db.Packages
            join order in db.Orders on package.OrderId equals order.Id
            where ids.Contains(order.DeliveryGroupId)
            select new { order.DeliveryGroupId, OrderId = order.Id, order.Status, package.HubId })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var timeZone = TenantTimeZone();

        return
        [
            .. groups.Select(group =>
            {
                var forDelivery = parcels
                    .Where(p => p.DeliveryGroupId == group.Id && Order.IsForDelivery(p.Status))
                    .ToList();

                return new ShelfRow(
                    DeliveryGroup.ShelfCode(hub.Code, group.Shelf!.Value),
                    group.Number,
                    group.Status,
                    LocalDay(group.LocksAt, timeZone),
                    forDelivery.Select(p => p.OrderId).Distinct().Count(),
                    forDelivery.Count(p => p.HubId == hub.Id),
                    forDelivery.Count);
            })
        ];
    }

    /// <summary>
    /// Reads the label, applies <paramref name="scan"/> to its order and saves. A save that loses to a parallel scan
    /// forgets what it loaded and scans again from fresh rows.
    /// </summary>
    private async Task<Result<ParcelScan>> ScanAsync(
        string? label,
        long? hubId,
        Func<Order, int, Task<Result<ScanOutcome>>> scan,
        CancellationToken cancellationToken)
    {
        if (!PackageLabel.TryParse(label, out var code))
        {
            return Error.Validation("hubScan.label.unreadable", $"\"{label?.Trim()}\" is not a parcel label.");
        }

        for (var attempt = 1; ; attempt++)
        {
            var order = await db.Orders
                .Include(o => o.Packages)
                .Include(o => o.DeliveryGroup)
                .FirstOrDefaultAsync(o => o.Number == code.OrderNumber, cancellationToken);
            if (order is null || order.Packages.All(p => p.Sequence != code.Sequence))
            {
                return Error.NotFound("hubScan.label.unknown", $"No parcel {code} is expected here.");
            }

            var outcome = await scan(order, code.Sequence);
            if (outcome.IsFailure)
            {
                return outcome.Error!;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                Forget(order);

                continue;
            }

            return await DescribeAsync(order, code, outcome.Value, hubId, cancellationToken);
        }
    }

    private async Task<ParcelScan> DescribeAsync(
        Order order,
        PackageLabel code,
        ScanOutcome outcome,
        long? hubId,
        CancellationToken cancellationToken)
    {
        var group = order.DeliveryGroup!;
        var merchant = await db.Merchants
            .Where(m => m.Id == order.MerchantId)
            .Select(m => m.Name)
            .SingleAsync(cancellationToken);
        var deliveryHub = await db.Hubs
            .Where(h => h.Id == group.HubId)
            .Select(h => h.Code)
            .SingleAsync(cancellationToken);
        var atDeliveryHub = hubId == group.HubId;

        return new ParcelScan(
            code.ToString(),
            order.Number,
            merchant,
            order.Packages.Count,
            order.Packages.Count(p => p.ReceivedOn is not null),
            order.Status,
            outcome == ScanOutcome.AlreadyRecorded,
            group.Kind != DeliveryGroupKind.Waiting,
            group.Number,
            group.Status,
            LocalDay(group.LocksAt, TenantTimeZone()),
            atDeliveryHub && group.Shelf is { } shelf ? DeliveryGroup.ShelfCode(deliveryHub, shelf) : null,
            hubId is not null && !atDeliveryHub ? deliveryHub : null);
    }

    /// <summary>
    /// Stops tracking a scan that lost a race; dependents first, so no required relationship is severed.
    /// </summary>
    private void Forget(Order order)
    {
        foreach (var history in order.History.ToList())
        {
            db.Entry(history).State = EntityState.Detached;
        }

        foreach (var package in order.Packages.ToList())
        {
            db.Entry(package).State = EntityState.Detached;
        }

        var group = order.DeliveryGroup!;
        db.Entry(order).State = EntityState.Detached;
        db.Entry(group).State = EntityState.Detached;
    }

    private Task<Hub?> FindHubAsync(string hubCode, CancellationToken cancellationToken)
    {
        return db.Hubs
            .AsNoTracking()
            .FirstOrDefaultAsync(hub => hub.Code == hubCode && !hub.Archived, cancellationToken);
    }

    private TimeZoneInfo TenantTimeZone()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Hub scans need a tenant.");

        return TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
    }

    private static DateOnly LocalDay(DateTime utc, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone));
    }
}
