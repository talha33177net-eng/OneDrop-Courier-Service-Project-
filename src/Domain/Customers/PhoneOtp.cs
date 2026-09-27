using System.Security.Cryptography;
using System.Text;
using Domain.Common;

namespace Domain.Customers;

/// <summary>
/// One login code sent by SMS. Only a hash of the code is stored; a challenge expires after a few minutes
/// and locks after a handful of wrong guesses, so the six-digit space cannot be walked.
/// </summary>
public class PhoneOtp : TenantEntity
{
    public const int CodeLength = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private PhoneOtp()
    {
    }

    private PhoneOtp(PhoneNumber phone, byte[] codeHash, DateTime expiresOn)
    {
        Phone = phone.Value;
        CodeHash = codeHash;
        ExpiresOn = expiresOn;
    }

    public string Phone { get; private set; } = "";

    public byte[] CodeHash { get; private set; } = [];

    public DateTime ExpiresOn { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? ConsumedOn { get; private set; }

    public static (PhoneOtp Challenge, string Code) Issue(PhoneNumber phone, DateTime now)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        return (new PhoneOtp(phone, Hash(phone.Value, code), now + Lifetime), code);
    }

    public bool IsOpen(DateTime now)
    {
        return ConsumedOn is null && Attempts < MaxAttempts && now < ExpiresOn;
    }

    public Result Verify(string? code, DateTime now)
    {
        if (!IsOpen(now))
        {
            return Error.Validation("otp.expired", "This code has expired. Ask for a new one.");
        }

        Attempts++;
        if (!CryptographicOperations.FixedTimeEquals(Hash(Phone, code ?? ""), CodeHash))
        {
            return Error.Validation("otp.wrong", "That code is not right.");
        }

        ConsumedOn = now;

        return Result.Success();
    }

    private static byte[] Hash(string phone, string code)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes($"{phone}:{code.Trim()}"));
    }
}
