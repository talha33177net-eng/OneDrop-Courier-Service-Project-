using System.Text;
using Domain.Common;

namespace Domain.Customers;

/// <summary>
/// A delivery address of one customer. A delivery group is phone + address, so home and office orders
/// travel separately. Two spellings of the same address must land on the same row, which is what
/// <see cref="MatchKey"/> is for.
/// </summary>
public class CustomerAddress : TenantEntity, IArchivable
{
    public const int MatchKeyLength = 400;

    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["house"] = "h",
        ["hs"] = "h",
        ["road"] = "r",
        ["rd"] = "r",
        ["block"] = "blk",
        ["sector"] = "sec",
        ["avenue"] = "ave",
        ["lane"] = "ln",
        ["flat"] = "f",
        ["floor"] = "fl",
        ["apartment"] = "apt",
        ["number"] = "no"
    };

    private CustomerAddress()
    {
    }

    public CustomerAddress(long customerId, long areaId, string line1, string? line2, string? landmark)
    {
        CustomerId = customerId;
        AreaId = areaId;
        Line1 = line1.Trim();
        Line2 = line2.NullIfBlank();
        Landmark = landmark.NullIfBlank();
        MatchKey = BuildMatchKey(line1, line2);
    }

    public long CustomerId { get; private set; }

    public long AreaId { get; private set; }

    public string Line1 { get; private set; } = "";

    public string? Line2 { get; private set; }

    /// <summary>Free text for the rider. Not part of the match: "near the mosque" is not an address.</summary>
    public string? Landmark { get; private set; }

    /// <summary>Normalised Line1 + Line2. Unique per customer and area.</summary>
    public string MatchKey { get; private set; } = "";

    /// <summary>
    /// Set once the customer said this spelling is the same place as another of their addresses: an order typed this
    /// way goes to that address instead (<c>CustomerDirectory</c>), so the spelling is learnt.
    /// </summary>
    public long? SameAsId { get; private set; }

    /// <summary>
    /// When the customer said this address is a place of its own, not their other address in the area, so they are not
    /// asked about it again.
    /// </summary>
    public DateTime? KeptApartOn { get; private set; }

    public bool Archived { get; private set; }

    /// <summary>
    /// The customer says this spelling is <paramref name="address"/>: from now on it stands for it. Only another
    /// address of the same customer in the same area can be the same place (home and office stay apart).
    /// </summary>
    public void SameAs(CustomerAddress address)
    {
        if (address.Id == Id || address.CustomerId != CustomerId || address.AreaId != AreaId || address.SameAsId is not null)
        {
            throw new InvalidOperationException("An address is only the same as another of its customer's in its area.");
        }

        SameAsId = address.Id;
    }

    /// <summary>The customer says this is a place of its own. Saying it again keeps the first time.</summary>
    public void KeepApart(DateTime now)
    {
        KeptApartOn ??= now;
    }

    /// <summary>
    /// Lower-cases, drops punctuation, splits letters from digits and shortens common words, so
    /// "House 12, Road-5" and "h12 rd 5" both become "h 12 r 5".
    /// </summary>
    public static string BuildMatchKey(string line1, string? line2)
    {
        var text = $"{line1} {line2}".ToLowerInvariant();
        var spaced = new StringBuilder(text.Length * 2);
        var previous = ' ';
        foreach (var character in text)
        {
            var current = char.IsLetterOrDigit(character) ? character : ' ';
            if (current != ' ' && previous != ' ' && char.IsDigit(current) != char.IsDigit(previous))
            {
                spaced.Append(' ');
            }

            spaced.Append(current);
            previous = current;
        }

        var words = spaced
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => Abbreviations.GetValueOrDefault(word, word));
        var key = string.Join(' ', words);

        return key.Length <= MatchKeyLength ? key : key[..MatchKeyLength];
    }
}
