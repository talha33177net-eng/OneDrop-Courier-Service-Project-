namespace Domain.Parcels;

/// <summary>
/// A problem a hub flagged on a parcel for the courier to look at. Stored as TINYINT; never renumber a saved value.
/// The parcel keeps its status and can still be scanned, sent between hubs and returned, but it does not go out to
/// the door until the flag is cleared.
/// </summary>
public enum ParcelIssue : byte
{
    /// <summary>The courier is checking something before the parcel goes on: the address, the contents, the packing.</summary>
    InReview = 1,

    /// <summary>Something has gone wrong that the courier must settle: the parcel is damaged, opened or missing.</summary>
    Exceptional = 2
}

public static class ParcelIssues
{
    /// <summary>How the problem is written for people: "In review".</summary>
    public static string DisplayName(this ParcelIssue issue)
    {
        return issue == ParcelIssue.InReview ? "In review" : "Exceptional";
    }
}
