using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Customers;
using Domain.Common;
using Domain.Customers;

namespace Application.Auth.PhoneLogin;

/// <summary>
/// Customer login without a password: a six-digit code by SMS. Proving the number also confirms the
/// customer's phone, which is what the "confirm the first grouping by OTP" rule relies on.
/// </summary>
public class PhoneLoginService(
    IAppDbContext db,
    ITenantContext tenantContext,
    CustomerDirectory customers,
    ISmsSender sms,
    TimeProvider time)
{
    public const int MaxCodesPerWindow = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public async Task<Result> RequestAsync(string? phoneInput, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.HasTenant)
        {
            return Error.Forbidden("otp.tenantRequired", "Open the login page on your city's OneDrop address.");
        }

        var phone = PhoneNumber.Parse(phoneInput);
        if (phone.IsFailure)
        {
            return phone.Error!;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var windowStart = now - Window;
        var recent = await db.PhoneOtps.CountAsync(
            o => o.Phone == phone.Value.Value && o.Created >= windowStart,
            cancellationToken);
        if (recent >= MaxCodesPerWindow)
        {
            return Error.Validation("otp.tooMany", "Too many codes requested. Try again in 15 minutes.");
        }

        var (challenge, code) = PhoneOtp.Issue(phone.Value, now);
        db.PhoneOtps.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        await sms.SendAsync(
            phone.Value,
            tenantContext.Tenant!.SmsSenderName,
            $"Your OneDrop login code is {code}. It expires in {PhoneOtp.Lifetime.TotalMinutes:0} minutes.",
            cancellationToken);

        return Result.Success();
    }

    /// <summary>Checks the code and returns the customer, creating the profile on first login.</summary>
    public async Task<Result<Customer>> VerifyAsync(
        string? phoneInput,
        string? code,
        CancellationToken cancellationToken = default)
    {
        var phone = PhoneNumber.Parse(phoneInput);
        if (phone.IsFailure)
        {
            return phone.Error!;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var challenge = await db.PhoneOtps
            .Where(o => o.Phone == phone.Value.Value && o.ConsumedOn == null && o.ExpiresOn > now)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (challenge is null)
        {
            return Error.Validation("otp.expired", "This code has expired. Ask for a new one.");
        }

        var verified = challenge.Verify(code, now);
        await db.SaveChangesAsync(cancellationToken);
        if (verified.IsFailure)
        {
            return verified.Error!;
        }

        var customer = await customers.FindOrCreateAsync(phone.Value, null, cancellationToken);
        customer.MarkPhoneVerified();
        await db.SaveChangesAsync(cancellationToken);

        return customer;
    }
}
