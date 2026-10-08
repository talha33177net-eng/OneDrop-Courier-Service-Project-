using Domain.Common;

namespace Domain.Merchants;

/// <summary>
/// The picture a business shows beside its name: its logo or the owner's photo. One per merchant (a main profile and each
/// business has its own), kept as the bytes the merchant uploaded, in a table of its own so that reading a merchant never
/// loads it. Only PNG, JPEG and WebP are accepted, judged by the file's first bytes and not by its name or declared type.
/// </summary>
public class MerchantPicture : TenantEntity, IMerchantOwned
{
    public const int MaxBytes = 512 * 1024;

    private MerchantPicture()
    {
    }

    public long MerchantId { get; private set; }

    public string ContentType { get; private set; } = "";

    public byte[] Content { get; private set; } = [];

    public static Result<MerchantPicture> Create(long merchantId, byte[]? content)
    {
        var picture = new MerchantPicture { MerchantId = merchantId };
        var replaced = picture.Replace(content);

        return replaced.IsSuccess ? picture : replaced.Error!;
    }

    public Result Replace(byte[]? content)
    {
        if (content is null || content.Length == 0)
        {
            return Error.Validation("merchant.picture.none", "Choose a picture to upload.");
        }

        if (content.Length > MaxBytes)
        {
            return Error.Validation("merchant.picture.size", $"The picture is too big. Use one under {MaxBytes / 1024} KB.");
        }

        var type = TypeOf(content);
        if (type is null)
        {
            return Error.Validation("merchant.picture.type", "Use a PNG, JPEG or WebP picture.");
        }

        ContentType = type;
        Content = content;

        return Result.Success();
    }

    private static string? TypeOf(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        // RIFF....WEBP
        if (bytes.Length > 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
