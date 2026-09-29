using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Customers;
using Application.Grouping;
using Domain.Common;
using Domain.Customers;
using Domain.Orders;

namespace Application.Orders.CreateOrder;

/// <summary>
/// Accepts an order from a merchant: validate, recognise the customer by phone, resolve the address to an
/// area, zone and hub, and save the order with its packages and first status in one SaveChanges, together
/// with the delivery group it joins or opens. The response never mentions the group: it would tell the merchant
/// whether the customer also bought elsewhere.
/// </summary>
public class CreateOrderHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    CustomerDirectory customers,
    DeliveryGrouping grouping,
    IValidator<CreateOrderCommand> validator)
{
    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<CreateOrderResult>> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant || !currentUser.MerchantId.HasValue)
        {
            return Error.Forbidden("order.merchantRequired", "Orders are created by a merchant of this tenant.");
        }

        var merchantId = currentUser.MerchantId.Value;
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var requestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, HashOptions));
        var replay = await FindReplayAsync(merchantId, command.IdempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var address = command.Address!;
        var areas = db.Areas
            .Include(a => a.Zone!)
            .ThenInclude(z => z.Hub)
            .Where(a => !a.Archived);
        var area = await (address.AreaId.HasValue
                ? areas.Where(a => a.Id == address.AreaId.Value)
                : areas.Where(a => a.Name == address.Area!.Trim()))
            .FirstOrDefaultAsync(cancellationToken);
        if (area is null)
        {
            return Error.Validation("order.area.unknown", "The area is not one of ours. See GET /api/v1/areas.");
        }

        var pickupPoints = db.PickupPoints.Where(p => p.MerchantId == merchantId && !p.Archived);
        var pickupPoint = await (command.PickupPointId.HasValue
                ? pickupPoints.Where(p => p.Id == command.PickupPointId.Value)
                : pickupPoints.Where(p => p.IsDefault))
            .FirstOrDefaultAsync(cancellationToken);
        if (pickupPoint is null)
        {
            return Error.Validation("order.pickupPoint.unknown", "No pickup point found. Set a default pickup point first.");
        }

        var customer = await customers.FindOrCreateAsync(
            PhoneNumber.Parse(command.Customer!.Phone).Value,
            command.Customer.Name,
            cancellationToken);
        var customerAddress = await customers.FindOrCreateAddressAsync(
            customer,
            area.Id,
            address.Line1!,
            address.Line2,
            address.Landmark,
            cancellationToken);

        var standing = await customers.StandingAsync(customer, cancellationToken);
        var created = Order.Create(new NewOrder(
            merchantId,
            customer.Id,
            customerAddress.Id,
            pickupPoint.Id,
            command.Customer.Name!,
            command.CodAmount,
            command.DeclaredValue ?? command.CodAmount,
            command.Speed,
            command.DoNotHold,
            [.. command.Packages.Select(p => new NewPackage(p.Description!, p.WeightGrams))])
        {
            ExternalReference = command.ExternalReference,
            IdempotencyKey = command.IdempotencyKey,
            RequestHash = requestHash,
            Note = command.Note,
            CustomerStep = standing.StepFor(
                command.CodAmount,
                command.FeeInAdvance,
                tenantContext.Tenant!.TrustedAfterDeliveries)
        });
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var order = created.Value;
        try
        {
            await grouping.SaveInGroupAsync(order, area.Zone!.HubId, cancellationToken);
        }
        catch (DbUpdateException) when (command.IdempotencyKey is not null)
        {
            // The same key raced us to the unique index: answer with the order that won
            db.Entry(order).State = EntityState.Detached;
            var winner = await FindReplayAsync(merchantId, command.IdempotencyKey, requestHash, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }

        return new CreateOrderResult(
            order.Id,
            order.Number,
            order.ExternalReference,
            order.Status,
            order.Speed,
            customer.Id,
            area.Name,
            area.Zone!.Name,
            area.Zone.Hub!.Name,
            order.Packages.Count,
            order.CodAmount,
            order.AddedFee,
            order.Created,
            order.ConfirmedOn == null ? order.CustomerStep : CustomerStep.None);
    }

    private async Task<Result<CreateOrderResult>?> FindReplayAsync(
        long merchantId,
        string? idempotencyKey,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var previous = await (
            from order in db.Orders
            join address in db.CustomerAddresses on order.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            join zone in db.Zones on area.ZoneId equals zone.Id
            join hub in db.Hubs on zone.HubId equals hub.Id
            where order.MerchantId == merchantId && order.IdempotencyKey == idempotencyKey
            select new
            {
                order.RequestHash,
                Result = new CreateOrderResult(
                    order.Id,
                    order.Number,
                    order.ExternalReference,
                    order.Status,
                    order.Speed,
                    order.CustomerId,
                    area.Name,
                    zone.Name,
                    hub.Name,
                    order.Packages.Count,
                    order.CodAmount,
                    order.AddedFee,
                    order.Created,
                    order.ConfirmedOn == null ? order.CustomerStep : CustomerStep.None)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (previous is null)
        {
            return null;
        }

        if (previous.RequestHash is null || !previous.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            return Error.Conflict(
                "order.idempotencyKey.reused",
                "This Idempotency-Key was already used for a different order.");
        }

        return previous.Result with { Replayed = true };
    }
}
