namespace Application.Abstractions;

/// <summary>
/// Who is calling. A merchant's API key has a <see cref="MerchantId"/> but no <see cref="UserId"/>;
/// background jobs have neither.
/// </summary>
public interface ICurrentUser
{
    long? UserId { get; }

    /// <summary>Set for merchant panel users and API keys. Switches on the merchant filter.</summary>
    long? MerchantId { get; }
}
