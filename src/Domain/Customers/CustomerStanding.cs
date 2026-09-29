namespace Domain.Customers;

/// <summary>
/// What a new order of a customer must wait for before its shop hands it over. Stored on the order as TINYINT; never
/// renumber a value that has been saved.
/// </summary>
public enum CustomerStep : byte
{
    /// <summary>Nothing: the order goes out as usual.</summary>
    None = 0,

    /// <summary>The customer confirms the order with one tap on the SMS link (a new customer, cash on delivery).</summary>
    Confirm = 1,

    /// <summary>The customer pays the delivery's first-shop fee in advance; the order is not collected until then.</summary>
    PayInAdvance = 2
}

/// <summary>
/// A customer's record at the operator: deliveries they accepted and visits that failed (a refusal at the door or
/// nobody home). It decides what a new cash-on-delivery order waits for. A product paid online never waits. A
/// customer with enough accepted deliveries (the tenant's setting) is trusted and never waits. Otherwise the fee is
/// paid in advance after a failed visit or when the shop asks, and a customer who has never accepted a delivery
/// confirms with one tap. The shop learns only what its own order waits for, never why.
/// </summary>
public sealed record CustomerStanding(int AcceptedDeliveries, int FailedVisits)
{
    public static CustomerStanding New => new(0, 0);

    public CustomerStep StepFor(decimal codAmount, bool shopAsksForAdvance, int trustedAfterDeliveries)
    {
        if (codAmount == 0 || AcceptedDeliveries >= trustedAfterDeliveries)
        {
            return CustomerStep.None;
        }

        if (FailedVisits > 0 || shopAsksForAdvance)
        {
            return CustomerStep.PayInAdvance;
        }

        return AcceptedDeliveries == 0 ? CustomerStep.Confirm : CustomerStep.None;
    }
}
