using System.Security.Cryptography;
using System.Text;
using Domain.Common;

namespace Domain.Merchants;

/// <summary>
/// A secret a merchant's website sends with every API call. The key reads od_{prefix}_{secret}: the prefix
/// is stored in clear and is how the key is found (before the tenant is known), the secret is only ever
/// stored as a SHA-256 hash. The plaintext is shown once, when the key is issued.
/// </summary>
public class MerchantApiKey : TenantEntity, IMerchantOwned
{
    public const string Scheme = "od";
    public const int PrefixLength = 12;
    public const int SecretLength = 32;

    private const string PrefixAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string SecretAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private MerchantApiKey()
    {
    }

    private MerchantApiKey(long merchantId, string name, string prefix, string secret)
    {
        MerchantId = merchantId;
        Name = name.Trim();
        Prefix = prefix;
        KeyHash = Hash(secret);
    }

    public long MerchantId { get; private set; }

    /// <summary>What the merchant calls the key, e.g. "Website".</summary>
    public string Name { get; private set; } = "";

    public string Prefix { get; private set; } = "";

    public byte[] KeyHash { get; private set; } = [];

    public DateTime? LastUsedOn { get; private set; }

    public DateTime? RevokedOn { get; private set; }

    public bool IsActive => RevokedOn is null;

    /// <summary>Issues a random key. Returns the plaintext, which is never stored.</summary>
    public static (MerchantApiKey Key, string Plaintext) Issue(long merchantId, string name)
    {
        var prefix = RandomNumberGenerator.GetString(PrefixAlphabet, PrefixLength);
        var secret = RandomNumberGenerator.GetString(SecretAlphabet, SecretLength);

        return (new MerchantApiKey(merchantId, name, prefix, secret), Format(prefix, secret));
    }

    /// <summary>Registers a known plaintext key. Used to give demo merchants stable, documented keys.</summary>
    public static Result<MerchantApiKey> FromPlaintext(long merchantId, string name, string plaintext)
    {
        if (!TryParse(plaintext, out var prefix, out var secret))
        {
            return Error.Validation("apikey.format", "An API key reads od_{12-character prefix}_{32-character secret}.");
        }

        return new MerchantApiKey(merchantId, name, prefix, secret);
    }

    /// <summary>Splits a presented key without touching the database. False for anything malformed.</summary>
    public static bool TryParse(string? plaintext, out string prefix, out string secret)
    {
        prefix = "";
        secret = "";
        var parts = plaintext?.Trim().Split('_');
        if (parts is not [Scheme, var candidatePrefix, var candidateSecret] ||
            candidatePrefix.Length != PrefixLength ||
            candidateSecret.Length != SecretLength ||
            !candidatePrefix.All(PrefixAlphabet.Contains) ||
            !candidateSecret.All(SecretAlphabet.Contains))
        {
            return false;
        }

        prefix = candidatePrefix;
        secret = candidateSecret;

        return true;
    }

    public bool Matches(string secret)
    {
        return IsActive && CryptographicOperations.FixedTimeEquals(Hash(secret), KeyHash);
    }

    public void MarkUsed(DateTime now)
    {
        LastUsedOn = now;
    }

    public void Revoke(DateTime now)
    {
        RevokedOn ??= now;
    }

    private static string Format(string prefix, string secret)
    {
        return $"{Scheme}_{prefix}_{secret}";
    }

    private static byte[] Hash(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
}
