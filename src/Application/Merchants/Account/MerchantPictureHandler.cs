using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Merchants;

namespace Application.Merchants.Account;

public sealed record PictureFile(string ContentType, byte[] Content);

/// <summary>
/// The picture of the business the merchant is working in (a main profile and each business have their own), and any
/// picture of the account's businesses for the switcher and the pages that show them.
/// </summary>
public class MerchantPictureHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Result> SetAsync(byte[]? content, CancellationToken cancellationToken = default)
    {
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("Only a merchant has a picture here.");
        var picture = await db.MerchantPictures.SingleOrDefaultAsync(p => p.MerchantId == merchantId, cancellationToken);
        if (picture is null)
        {
            var created = MerchantPicture.Create(merchantId, content);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            db.MerchantPictures.Add(created.Value);
        }
        else
        {
            var replaced = picture.Replace(content);
            if (replaced.IsFailure)
            {
                return replaced;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("Only a merchant has a picture here.");
        var picture = await db.MerchantPictures.SingleOrDefaultAsync(p => p.MerchantId == merchantId, cancellationToken);
        if (picture is not null)
        {
            db.MerchantPictures.Remove(picture);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>When the working business's picture last changed; null when it has none.</summary>
    public async Task<DateTime?> ChangedAsync(CancellationToken cancellationToken = default)
    {
        return await db.MerchantPictures.Select(p => (DateTime?)p.UpdatedOn).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The picture of a business of this login's account; nothing for anyone else's or when it has none.</summary>
    public async Task<PictureFile?> GetAsync(long merchantId, CancellationToken cancellationToken = default)
    {
        var account = currentUser.AccountId ?? throw new InvalidOperationException("Only a merchant login has pictures here.");

        return await (
            from picture in db.MerchantPictures.IgnoreQueryFilters([QueryFilters.Merchant])
            join merchant in db.Merchants.IgnoreQueryFilters([QueryFilters.Merchant]) on picture.MerchantId equals merchant.Id
            where picture.MerchantId == merchantId && (merchant.Id == account || merchant.MainMerchantId == account)
            select new PictureFile(picture.ContentType, picture.Content))
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }
}
