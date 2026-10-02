using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Customers;
using Domain.Grouping;
using Domain.Payments;

namespace Application.Grouping.CombineDeliveries;

/// <summary>
/// "Same address as your delivery <see cref="OtherDelivery"/>?": the customer has two deliveries on their way in one
/// area to addresses spelt differently. <see cref="Delivery"/> goes to the newer spelling, <see cref="Address"/>.
/// Combined, everything arrives on <see cref="CombinedDay"/> (the sooner of the two, in the tenant's time zone) for
/// one fee.
/// </summary>
public sealed record SameAddressQuestion(
    string Delivery,
    string Address,
    string OtherDelivery,
    string OtherAddress,
    DateOnly CombinedDay)
{
    public bool IsAbout(string delivery)
    {
        return Delivery == delivery || OtherDelivery == delivery;
    }
}

/// <summary>
/// Two spellings of one address make two deliveries and, on different days, two fees. When a customer has deliveries
/// on their way to two addresses in the same area, they are asked whether it is the same place (the order's SMS, the
/// order page and "My deliveries"). **Combine**: the deliveries become one (<see cref="DeliveryGroup.Combine"/>), its
/// fee is worked out again on every shop in it, and the other spelling stands for the kept address from then on, so
/// the next order matches by itself. **Keep separate**: the newer address is a place of its own and is not asked about
/// again. Deliveries in different areas are never asked about (home and office stay apart).
///
/// Only deliveries still waiting at the hub and not yet on any trip are offered: the rider's visit already makes
/// deliveries on one trip one stop. Two that each hold a shelf, or each have an advance asked for, are not offered
/// either: the parcels or the payments would have to be sorted out by hand. The question is worked out from the data
/// whenever it is shown, so nothing has to be kept in step.
/// </summary>
public class CombineDeliveriesHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    public static Error NotFound(string delivery) =>
        Error.NotFound("deliveryGroup.notFound", $"Delivery {delivery} was not found.");

    /// <summary>The customer's open questions, one per delivery to a newer spelling.</summary>
    public async Task<IReadOnlyList<SameAddressQuestion>> QuestionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(Tenant.TimeZone);

        return [.. Pairs(await CandidatesAsync(customerId, cancellationToken)).Select(pair => new SameAddressQuestion(
            pair.Asked.Number,
            pair.Asked.Address,
            pair.Other.Number,
            pair.Other.Address,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                pair.Asked.LocksAt < pair.Other.LocksAt ? pair.Asked.LocksAt : pair.Other.LocksAt,
                timeZone))))];
    }

    /// <summary>
    /// The customer says yes for the question about <paramref name="delivery"/> (either of its two deliveries): the
    /// two become one, the number of the delivery that goes on. Another customer's delivery is not found.
    /// </summary>
    public async Task<Result<string>> CombineAsync(
        long customerId,
        string delivery,
        CancellationToken cancellationToken = default)
    {
        var pair = await PairAboutAsync(customerId, delivery, cancellationToken);
        if (pair.IsFailure)
        {
            return pair.Error!;
        }

        var ids = new[] { pair.Value.Asked.GroupId, pair.Value.Other.GroupId };
        var groups = await db.DeliveryGroups.Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken);
        var combined = DeliveryGroup.Combine(groups[0], groups[1]);
        if (combined.IsFailure)
        {
            return combined.Error!;
        }

        var (keeps, ends, shelf) = combined.Value;
        var now = time.GetUtcNow().UtcDateTime;

        // The cancelled delivery's spelling, and any spelling already standing for it, now stand for the kept address
        var addresses = await db.CustomerAddresses
            .Where(a => a.Id == keeps.AddressId || a.Id == ends.AddressId || a.SameAsId == ends.AddressId)
            .ToListAsync(cancellationToken);
        var kept = addresses.Single(a => a.Id == keeps.AddressId);
        foreach (var spelling in addresses.Where(a => a.Id != kept.Id))
        {
            spelling.SameAs(kept);
        }

        // The orders are other shops' too: each moves, and nothing about them leaves this method
        var orders = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(o => o.DeliveryGroupId == ends.Id || o.DeliveryGroupId == keeps.Id)
            .ToListAsync(cancellationToken);
        foreach (var order in orders.Where(o => o.DeliveryGroupId == ends.Id))
        {
            order.CombineInto(keeps);
        }

        // One advance at most between the two (an offered pair never has two): it now covers the kept delivery, and
        // once paid it releases every order of it, as paying it would have
        var advances = await db.Payments
            .Where(p => ids.Contains(p.DeliveryGroupId) &&
                p.Purpose == PaymentPurpose.Advance &&
                p.Status != PaymentStatus.Cancelled)
            .ToListAsync(cancellationToken);
        foreach (var advance in advances)
        {
            advance.CoverInstead(keeps.Id);
        }

        if (advances.Any(p => p.Status == PaymentStatus.Paid))
        {
            orders.ForEach(order => order.AdvancePaid(now));
        }

        // Shelves are unique per hub: the cancelled delivery gives its shelf up in the first save, the kept one takes it
        // in the second
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (shelf is { } freed)
            {
                keeps.PutOnShelf(freed);
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A rider's planner, a scan or the lock job changed one of them since it was read
            return DeliveryGroup.NotCombinable;
        }

        return keeps.Number;
    }

    /// <summary>
    /// The customer says no for the question about <paramref name="delivery"/>: the newer address is a place of its
    /// own. Saying it when nothing is asked any more changes nothing.
    /// </summary>
    public async Task<Result> KeepSeparateAsync(
        long customerId,
        string delivery,
        CancellationToken cancellationToken = default)
    {
        var pair = await PairAboutAsync(customerId, delivery, cancellationToken);
        if (pair.IsFailure)
        {
            return pair.Error == DeliveryGroup.NotCombinable ? Result.Success() : pair.Error!;
        }

        var address = await db.CustomerAddresses.SingleAsync(a => a.Id == pair.Value.Asked.AddressId, cancellationToken);
        address.KeepApart(time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private TenantInfo Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Deliveries need a tenant.");

    /// <summary>
    /// For each delivery to an address not settled yet (neither the same as another nor kept apart), the delivery to an
    /// older address of the customer in the same area that leaves soonest and can be combined with it. The newer
    /// spelling is the one asked about, so a "keep separate" answers the pair for good.
    /// </summary>
    private static IEnumerable<(Candidate Asked, Candidate Other)> Pairs(IReadOnlyList<Candidate> candidates)
    {
        foreach (var asked in candidates.Where(c => c.KeptApartOn is null))
        {
            var other = candidates
                .Where(c => c.AreaId == asked.AreaId &&
                    c.AddressId < asked.AddressId &&
                    !(c.Shelved && asked.Shelved) &&
                    !(c.HasAdvance && asked.HasAdvance))
                .OrderBy(c => c.LocksAt)
                .ThenBy(c => c.GroupId)
                .FirstOrDefault();
            if (other is not null)
            {
                yield return (asked, other);
            }
        }
    }

    private async Task<Result<(Candidate Asked, Candidate Other)>> PairAboutAsync(
        long customerId,
        string delivery,
        CancellationToken cancellationToken)
    {
        var pairs = Pairs(await CandidatesAsync(customerId, cancellationToken)).ToList();
        var pair = pairs.FirstOrDefault(p => p.Asked.Number == delivery);
        if (pair == default)
        {
            pair = pairs.FirstOrDefault(p => p.Other.Number == delivery);
        }

        if (pair != default)
        {
            return pair;
        }

        var known = await db.DeliveryGroups.AnyAsync(g => g.Number == delivery && g.CustomerId == customerId, cancellationToken);

        return known ? DeliveryGroup.NotCombinable : NotFound(delivery);
    }

    /// <summary>The customer's deliveries that could still be combined: waiting at the hub, never on a trip.</summary>
    private async Task<IReadOnlyList<Candidate>> CandidatesAsync(long customerId, CancellationToken cancellationToken)
    {
        return await (
            from g in db.DeliveryGroups
            join address in db.CustomerAddresses on g.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where g.CustomerId == customerId &&
                (g.Status == DeliveryGroupStatus.Open || g.Status == DeliveryGroupStatus.Locked) &&
                address.SameAsId == null &&
                !db.TripStops.Any(stop => stop.DeliveryGroupId == g.Id)
            orderby g.LocksAt, g.Id
            select new Candidate
            {
                GroupId = g.Id,
                Number = g.Number,
                LocksAt = g.LocksAt,
                AddressId = address.Id,
                AreaId = address.AreaId,
                Address = (address.Line2 == null ? address.Line1 : address.Line1 + ", " + address.Line2) + ", " + area.Name,
                KeptApartOn = address.KeptApartOn,
                Shelved = g.Shelf != null,
                HasAdvance = db.Payments.Any(p => p.DeliveryGroupId == g.Id &&
                    p.Purpose == PaymentPurpose.Advance &&
                    p.Status != PaymentStatus.Cancelled)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private sealed record Candidate
    {
        public long GroupId { get; init; }

        public string Number { get; init; } = "";

        public DateTime LocksAt { get; init; }

        public long AddressId { get; init; }

        public long AreaId { get; init; }

        public string Address { get; init; } = "";

        public DateTime? KeptApartOn { get; init; }

        public bool Shelved { get; init; }

        public bool HasAdvance { get; init; }
    }
}
