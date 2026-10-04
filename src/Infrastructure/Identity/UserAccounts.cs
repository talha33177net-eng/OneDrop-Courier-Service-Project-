using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;

namespace Infrastructure.Identity;

/// <summary>
/// Creates logins for the current tenant through Identity, on the same context as the use case, so a use case's
/// transaction covers the login too.
/// </summary>
public class UserAccounts(UserManager<AppUser> users, ITenantContext tenantContext, TimeProvider time) : IUserAccounts
{
    public async Task<Result<long>> CreateAsync(NewLogin login, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new InvalidOperationException("A login belongs to a tenant.");
        var email = login.Email?.Trim();
        if (string.IsNullOrEmpty(email) || email.Length > 256 || !email.Contains('@'))
        {
            return Error.Validation("login.email", "Enter a valid email address to sign in with.");
        }

        // The user filter keeps the lookup to this tenant: the same email may sign in at another courier
        if (await users.FindByNameAsync(email) is not null)
        {
            return Error.Conflict("login.email.taken", "This email already has an account here. Sign in instead.");
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = login.DisplayName,
            TenantId = tenantId,
            MerchantId = login.MerchantId,
            Created = time.GetUtcNow().UtcDateTime
        };
        var created = await users.CreateAsync(user, login.Password ?? "");
        if (created.Succeeded)
        {
            created = await users.AddToRoleAsync(user, login.Role);
        }

        return created.Succeeded
            ? user.Id
            : Error.Validation("login.password", string.Join(" ", created.Errors.Select(e => e.Description)));
    }

    public async Task<IReadOnlyDictionary<long, string>> EmailsAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken = default)
    {
        return await users.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? "", cancellationToken);
    }

    public async Task<string?> MerchantEmailAsync(long merchantId, CancellationToken cancellationToken = default)
    {
        return await users.Users
            .Where(u => u.MerchantId == merchantId)
            .OrderBy(u => u.Id)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
