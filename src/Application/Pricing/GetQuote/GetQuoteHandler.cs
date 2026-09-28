using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Grouping;
using Domain.Common;
using Domain.Customers;

namespace Application.Pricing.GetQuote;

/// <summary>
/// The delivery fee a checkout shows before the order is sent: the base fee for a new delivery, the extra-shop fee
/// when the customer already has one on its way to that address, nothing when this shop is already in it. Read
/// only: the customer and address are looked up, never created, so a quote leaves no trace. The answer is the fee
/// Create Order would give the same order at the same moment.
/// </summary>
public class GetQuoteHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    DeliveryGrouping grouping,
    IValidator<GetQuoteQuery> validator)
{
    public async Task<Result<QuoteResult>> HandleAsync(GetQuoteQuery query, CancellationToken cancellationToken = default)
    {
        if (tenantContext.Tenant is not { } tenant || !currentUser.MerchantId.HasValue)
        {
            return Error.Forbidden("quote.merchantRequired", "Quotes are given to a merchant of this tenant.");
        }

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var areas = db.Areas.Where(a => !a.Archived);
        var areaId = await (query.AreaId.HasValue
                ? areas.Where(a => a.Id == query.AreaId.Value)
                : areas.Where(a => a.Name == query.Area!.Trim()))
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (areaId is null)
        {
            return Error.Validation("quote.area.unknown", "The area is not one of ours. See GET /api/v1/areas.");
        }

        var phone = PhoneNumber.Parse(query.Phone).Value.Value;
        var matchKey = CustomerAddress.BuildMatchKey(query.Line1!, query.Line2);
        var known = await db.CustomerAddresses
            .Where(a => a.AreaId == areaId && a.MatchKey == matchKey)
            .Join(
                db.Customers.Where(c => c.Phone == phone),
                address => address.CustomerId,
                customer => customer.Id,
                (address, customer) => new { address.CustomerId, address.Id })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        var quote = await grouping.QuoteAsync(
            new QuoteRequest(currentUser.MerchantId.Value, known?.CustomerId, known?.Id, query.Speed, query.DoNotHold),
            cancellationToken);

        return new QuoteResult(quote.Fee, tenant.CurrencyCode, quote.JoinsDelivery);
    }
}
