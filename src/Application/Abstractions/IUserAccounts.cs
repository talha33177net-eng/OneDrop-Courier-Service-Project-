using Domain.Common;

namespace Application.Abstractions;

/// <summary>A login to create for the current tenant: a merchant's user (with <see cref="MerchantId"/>), a rider or staff.</summary>
public sealed record NewLogin(string? Email, string? Password, string DisplayName, string Role)
{
    public long? MerchantId { get; init; }
}

/// <summary>Creates logins. Identity lives in Infrastructure; the use cases only ask for an account.</summary>
public interface IUserAccounts
{
    /// <summary>The new login's id, or why it was refused (email taken at this courier, weak password).</summary>
    Task<Result<long>> CreateAsync(NewLogin login, CancellationToken cancellationToken = default);

    /// <summary>The emails of the logins with these ids, for showing who can sign in.</summary>
    Task<IReadOnlyDictionary<long, string>> EmailsAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>The email of a merchant's first login, or null.</summary>
    Task<string?> MerchantEmailAsync(long merchantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets a login in or shuts it out, for a person stopped by whoever added them. A shut-out login is refused at
    /// sign-in, and a session already open ends at its next security check.
    /// </summary>
    Task SetLoginEnabledAsync(long userId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Gives a login a new password, for an owner who has to hand one out again.</summary>
    Task<Result> ResetPasswordAsync(long userId, string password, CancellationToken cancellationToken = default);
}
