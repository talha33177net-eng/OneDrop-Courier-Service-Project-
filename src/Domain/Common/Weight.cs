using System.Globalization;

namespace Domain.Common;

public static class Weight
{
    /// <summary>Grams as people read them: "1.5 kg".</summary>
    public static string Kg(int grams)
    {
        return (grams / 1000m).ToString("0.##", CultureInfo.InvariantCulture) + " kg";
    }
}
