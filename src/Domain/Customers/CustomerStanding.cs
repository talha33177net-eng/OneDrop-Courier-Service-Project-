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
/// The tenant's trust settings: <see cref="TrustedAfterDeliveries"/> accepted deliveries with no failure since and a
/// customer never pays in advance, even when a shop asks; after a failure the fee is paid in advance until
/// <see cref="TrustedAgainAfterDeliveries"/> deliveries have been accepted since it.
/// </summary>
public sealed record TrustRules(int TrustedAfterDeliveries, int TrustedAgainAfterDeliveries);

/// <summary>
/// A customer's record at the operator, across every shop: deliveries they accepted, visits that failed (a refusal at
/// the door or nobody home) and the deliveries accepted since the last failure (all of them when there was none). It
/// decides what a new cash-on-delivery order waits for. A product paid online never waits. A customer with enough
/// accepted deliveries since their last failure is trusted and never waits. Otherwise the fee is paid in advance while
/// a failure is recent (too few deliveries accepted since) or when the shop asks, and a customer who has never
/// accepted a delivery confirms with one tap. The shop learns only what its own order waits for, never why.
/// </summary>
public sealed record CustomerStanding(int AcceptedDeliveries, int FailedVisits, int AcceptedSinceLastFailure)
{
    public static CustomerStanding New => new(0, 0, 0);

    /// <summary>A customer who has never failed a visit: every accepted delivery counts since the last failure.</summary>
    public static CustomerStanding Clean(int acceptedDeliveries) => new(acceptedDeliveries, 0, acceptedDeliveries);

    public CustomerStep StepFor(decimal codAmount, bool shopAsksForAdvance, TrustRules rules)
    {
        if (codAmount == 0 || AcceptedSinceLastFailure >= rules.TrustedAfterDeliveries)
        {
            return CustomerStep.None;
        }

        if ((FailedVisits > 0 && AcceptedSinceLastFailure < rules.TrustedAgainAfterDeliveries) || shopAsksForAdvance)
        {
            return CustomerStep.PayInAdvance;
        }

        return AcceptedDeliveries == 0 ? CustomerStep.Confirm : CustomerStep.None;
    }
}
