using System.Security.Cryptography;
using System.Text;

namespace Domain.Merchants;

/// <summary>
/// How a shop checks that a webhook came from us, as the Standard Webhooks specification (standardwebhooks.com) has it,
/// so a shop can use any of its libraries: the secret is <c>whsec_</c> + base64 of random bytes, and the signature is
/// <c>v1,</c> + base64 of HMAC-SHA256 over <c>{id}.{timestamp}.{body}</c> keyed with the decoded secret.
/// </summary>
public static class WebhookSignature
{
    public const string SecretPrefix = "whsec_";
    public const int SecretBytes = 24;

    public static string NewSecret()
    {
        return SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    /// <summary>The <c>webhook-signature</c> header for a message; <paramref name="timestamp"/> is Unix seconds.</summary>
    public static string Sign(string secret, string id, long timestamp, string body)
    {
        if (!secret.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("A webhook secret starts with whsec_.", nameof(secret));
        }

        var key = Convert.FromBase64String(secret[SecretPrefix.Length..]);
        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));

        return "v1," + Convert.ToBase64String(mac);
    }
}
