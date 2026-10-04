using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Merchants;

namespace Application.Merchants.ApiKeys;

/// <summary>One of the shop's keys as it is shown, times in the operator's time zone; never the secret, which is not stored.</summary>
public sealed record ApiKeyRow(string Name, string Prefix, DateTime Created, DateTime? LastUsed, DateTime? Revoked);

/// <summary>
/// The signed-in shop's API keys: list them, issue a new one (its plaintext returned once, never stored) and revoke one.
/// A revoked key stops working on its next call. Another shop's key is "not found".
/// </summary>
public class MerchantApiKeysHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider time)
{
    public static readonly Error NotFound = Error.NotFound("apikey.notFound", "That key was not found.");

    public async Task<IReadOnlyList<ApiKeyRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("API keys need a tenant.");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var keys = await db.MerchantApiKeys
            .OrderBy(key => key.RevokedOn != null)
            .ThenByDescending(key => key.Id)
            .Select(key => new ApiKeyRow(key.Name, key.Prefix, key.Created, key.LastUsedOn, key.RevokedOn))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. keys.Select(key => key with
            {
                Created = Local(key.Created),
                LastUsed = key.LastUsed is { } used ? Local(used) : null,
                Revoked = key.Revoked is { } revoked ? Local(revoked) : null
            })
        ];

        DateTime Local(DateTime utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
        }
    }

    /// <summary>A new key for the shop; the plaintext is in the result and nowhere else.</summary>
    public async Task<Result<string>> IssueAsync(string? name, CancellationToken cancellationToken = default)
    {
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("Only a shop has API keys.");
        var created = MerchantApiKey.Create(merchantId, name);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.MerchantApiKeys.Add(created.Value.Key);
        await db.SaveChangesAsync(cancellationToken);

        return created.Value.Plaintext;
    }

    /// <summary>Revokes the shop's key with this prefix; the merchant filter makes any other shop's key not found.</summary>
    public async Task<Result> RevokeAsync(string? prefix, CancellationToken cancellationToken = default)
    {
        var key = await db.MerchantApiKeys.SingleOrDefaultAsync(k => k.Prefix == prefix, cancellationToken);
        if (key is null)
        {
            return NotFound;
        }

        key.Revoke(time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
