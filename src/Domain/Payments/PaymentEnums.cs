namespace Domain.Payments;

/// <summary>How the customer paid. Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum PaymentMethod : byte
{
    /// <summary>Cash to the rider, handed in at the hub.</summary>
    Cash = 1,

    /// <summary>bKash, by scanning the QR on the rider's phone.</summary>
    Bkash = 2,

    /// <summary>Nagad, by scanning the QR on the rider's phone.</summary>
    Nagad = 3
}

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum PaymentStatus : byte
{
    /// <summary>A QR shown to the customer, not paid yet.</summary>
    Pending = 1,

    Paid = 2,

    /// <summary>A QR no longer wanted: the customer paid another way or the amount changed before paying.</summary>
    Cancelled = 3
}

/// <summary>What a payment is for. Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum PaymentPurpose : byte
{
    /// <summary>The delivery fee and the shops' cash on delivery, paid at the door.</summary>
    Door = 1
}

public static class PaymentMethodNames
{
    /// <summary>How the method is written for people: "cash", "bKash", "Nagad".</summary>
    public static string DisplayName(this PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.Bkash => "bKash",
            PaymentMethod.Nagad => "Nagad",
            _ => "cash"
        };
    }
}
