using System.Text.RegularExpressions;

namespace Domain.Orders;

/// <summary>
/// The code printed on a parcel, as text and as a QR code: the order number and the package's sequence,
/// <c>OD-100001-1</c>. Every scan (pickup, hub, rider) reads it back with <see cref="TryParse"/>. It names no tenant:
/// the scanner's tenant decides whose parcel it may be.
/// </summary>
public readonly partial record struct PackageLabel(string OrderNumber, int Sequence)
{
    public override string ToString()
    {
        return $"{OrderNumber}-{Sequence}";
    }

    /// <summary>Accepts what a scanner or a person typing may send: spaces around it, lower case.</summary>
    public static bool TryParse(string? text, out PackageLabel label)
    {
        label = default;
        var match = Code().Match(text?.Trim() ?? "");
        if (!match.Success ||
            !int.TryParse(match.Groups["sequence"].Value, out var sequence) ||
            sequence is < 1 or > Order.MaxPackages)
        {
            return false;
        }

        label = new PackageLabel(match.Groups["order"].Value.ToUpperInvariant(), sequence);

        return true;
    }

    [GeneratedRegex(@"^(?<order>OD-\d{6,})-(?<sequence>\d{1,2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Code();
}
