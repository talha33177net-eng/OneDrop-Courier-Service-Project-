using Domain.Merchants;

namespace Domain.Tests;

/// <summary>The picture of a business: only real PNG, JPEG and WebP files of a sensible size.</summary>
public class MerchantPictureTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0];
    private static readonly byte[] Webp = [.. "RIFF"u8, 1, 0, 0, 0, .. "WEBP"u8, 0];

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public void A_picture_is_known_by_its_first_bytes(string kind, string type)
    {
        var bytes = kind switch { "png" => Png, "jpeg" => Jpeg, _ => Webp };

        var picture = MerchantPicture.Create(7, bytes);

        Assert.True(picture.IsSuccess);
        Assert.Equal(type, picture.Value.ContentType);
        Assert.Equal(7, picture.Value.MerchantId);
    }

    [Fact]
    public void Anything_else_is_refused_whatever_it_is_called()
    {
        Assert.Equal("merchant.picture.type", MerchantPicture.Create(7, "<svg onload=alert(1)>"u8.ToArray()).Error!.Code);
        Assert.Equal("merchant.picture.none", MerchantPicture.Create(7, []).Error!.Code);
        Assert.Equal("merchant.picture.none", MerchantPicture.Create(7, null).Error!.Code);
    }

    [Fact]
    public void A_picture_over_the_limit_is_refused_and_a_replacement_keeps_the_old_one_when_refused()
    {
        var picture = MerchantPicture.Create(7, Png).Value;

        Assert.Equal("merchant.picture.size", picture.Replace(new byte[MerchantPicture.MaxBytes + 1]).Error!.Code);
        Assert.Equal("image/png", picture.ContentType);
        Assert.True(picture.Replace(Jpeg).IsSuccess);
        Assert.Equal("image/jpeg", picture.ContentType);
    }
}
