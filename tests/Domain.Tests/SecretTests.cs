using Domain.Customers;
using Domain.Merchants;

namespace Domain.Tests;

public class MerchantApiKeyTests
{
    [Fact]
    public void An_issued_key_matches_its_own_secret_only()
    {
        var (key, plaintext) = MerchantApiKey.Issue(merchantId: 3, "Website");

        Assert.True(MerchantApiKey.TryParse(plaintext, out var prefix, out var secret));
        Assert.Equal(key.Prefix, prefix);
        Assert.True(key.Matches(secret));
        Assert.False(key.Matches(secret[..^1] + (secret[^1] == 'a' ? 'b' : 'a')));
    }

    [Fact]
    public void The_plaintext_secret_is_never_stored()
    {
        var (key, plaintext) = MerchantApiKey.Issue(3, "Website");
        MerchantApiKey.TryParse(plaintext, out _, out var secret);

        var storedText = typeof(MerchantApiKey)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(key));

        Assert.DoesNotContain(storedText, value => value?.Contains(secret, StringComparison.Ordinal) == true);
        Assert.Equal(32, key.KeyHash.Length);
    }

    [Fact]
    public void A_revoked_key_no_longer_matches()
    {
        var (key, plaintext) = MerchantApiKey.Issue(3, "Website");
        MerchantApiKey.TryParse(plaintext, out _, out var secret);

        key.Revoke(DateTime.UtcNow);

        Assert.False(key.Matches(secret));
    }

    [Fact]
    public void A_shop_names_its_key_and_gets_a_working_one()
    {
        var created = MerchantApiKey.Create(3, "  Shopify store  ");

        var (key, plaintext) = created.Value;
        Assert.Equal("Shopify store", key.Name);
        Assert.Equal(3, key.MerchantId);
        Assert.True(MerchantApiKey.TryParse(plaintext, out _, out var secret));
        Assert.True(key.Matches(secret));
        Assert.NotEqual(plaintext, MerchantApiKey.Create(3, "Shopify store").Value.Plaintext);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void A_key_needs_a_name_of_reasonable_length(string? name)
    {
        Assert.Equal("apikey.name", MerchantApiKey.Create(3, name).Error!.Code);
        Assert.Equal("apikey.name", MerchantApiKey.Create(3, new string('a', MerchantApiKey.MaxNameLength + 1)).Error!.Code);
        Assert.True(MerchantApiKey.Create(3, new string('a', MerchantApiKey.MaxNameLength)).IsSuccess);
    }

    [Fact]
    public void Revoking_again_keeps_the_first_date()
    {
        var (key, _) = MerchantApiKey.Issue(3, "Website");
        var first = new DateTime(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);

        key.Revoke(first);
        key.Revoke(first.AddHours(1));

        Assert.Equal(first, key.RevokedOn);
        Assert.False(key.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("od_short_DevOnlyKeyDoNotUseInProduction01")]
    [InlineData("xx_dhkfashion01_DevOnlyKeyDoNotUseInProduction01")]
    [InlineData("od_DHKFASHION01_DevOnlyKeyDoNotUseInProduction01")]
    [InlineData("od_dhkfashion01_DevOnlyKeyDoNotUseInProduction0!")]
    [InlineData("od_dhkfashion01_DevOnlyKey")]
    public void Malformed_keys_are_rejected_before_any_lookup(string? presented)
    {
        Assert.False(MerchantApiKey.TryParse(presented, out _, out _));
    }
}

public class PhoneOtpTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Parse("01712345678").Value;
    private static readonly DateTime Now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_right_code_signs_in_once()
    {
        var (challenge, code) = PhoneOtp.Issue(Phone, Now);

        Assert.True(challenge.Verify(code, Now.AddMinutes(1)).IsSuccess);
        Assert.Equal("otp.expired", challenge.Verify(code, Now.AddMinutes(1)).Error!.Code);
    }

    [Fact]
    public void A_code_expires()
    {
        var (challenge, code) = PhoneOtp.Issue(Phone, Now);

        Assert.Equal("otp.expired", challenge.Verify(code, Now + PhoneOtp.Lifetime).Error!.Code);
    }

    [Fact]
    public void Wrong_guesses_lock_the_code()
    {
        var (challenge, code) = PhoneOtp.Issue(Phone, Now);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < PhoneOtp.MaxAttempts; attempt++)
        {
            Assert.Equal("otp.wrong", challenge.Verify(wrong, Now).Error!.Code);
        }

        Assert.Equal("otp.expired", challenge.Verify(code, Now).Error!.Code);
    }
}
