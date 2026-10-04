using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Parcels;

namespace Application.Parcels.FraudCheck;

/// <summary>How a phone number's parcels have ended at this courier, across every merchant.</summary>
public sealed record FraudCheckResult(string Phone, int Total, int Delivered, int Returned, int InProgress)
{
    /// <summary>Delivered out of the parcels that have ended (delivered or returned), as a whole per cent; null with none.</summary>
    public int? SuccessRate => Delivered + Returned == 0 ? null : (int)Math.Round(100m * Delivered / (Delivered + Returned));

    public string Verdict => SuccessRate switch
    {
        null => "New customer",
        >= 80 => "Reliable",
        >= 50 => "Mixed record",
        _ => "High risk"
    };
}

/// <summary>
/// The merchant's fraud check: before sending a cash-on-delivery parcel, see how often a phone number took its parcels.
/// Counts every merchant's parcels of the courier, so one shop's refusals warn the others, but answers with counts only:
/// never which shops, what or when.
/// </summary>
public class FraudCheckHandler(IAppDbContext db)
{
    public async Task<Result<FraudCheckResult>> CheckAsync(string? phone, CancellationToken cancellationToken = default)
    {
        var parsed = PhoneNumber.Parse(phone);
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        var value = parsed.Value.Value;

        // Across the courier's merchants on purpose: a count of outcomes, with nothing that names another shop
        var counts = await db.Parcels
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(p => p.RecipientPhone == value && p.Status != ParcelStatus.Cancelled && p.Status != ParcelStatus.Pending)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        var delivered = counts.GetValueOrDefault(ParcelStatus.Delivered) + counts.GetValueOrDefault(ParcelStatus.PartlyDelivered);
        var returned = counts.GetValueOrDefault(ParcelStatus.Returned) + counts.GetValueOrDefault(ParcelStatus.Returning);

        return new FraudCheckResult(parsed.Value.Local, counts.Values.Sum(), delivered, returned, counts.Values.Sum() - delivered - returned);
    }
}
