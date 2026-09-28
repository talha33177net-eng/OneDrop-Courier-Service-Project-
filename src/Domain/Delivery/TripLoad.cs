namespace Domain.Delivery;

/// <summary>What a trip, or one delivery on it, puts on a bike: parcels and their weight.</summary>
public readonly record struct TripLoad(int Parcels, int WeightGrams)
{
    public static TripLoad None => new(0, 0);

    public static TripLoad operator +(TripLoad left, TripLoad right)
    {
        return new TripLoad(left.Parcels + right.Parcels, left.WeightGrams + right.WeightGrams);
    }

    /// <summary>True when this load stays within <paramref name="limit"/> on both counts.</summary>
    public bool FitsIn(TripLoad limit)
    {
        return Parcels <= limit.Parcels && WeightGrams <= limit.WeightGrams;
    }
}
