namespace Application.Abstractions;

/// <summary>
/// Addresses of the customer's own pages, for links in SMS. Built from a configured format per tenant subdomain, not
/// from the current request: background work (the outbox) has none.
/// </summary>
public interface ICustomerLinks
{
    /// <summary>The page where the customer confirms an order or pays its delivery fee in advance.</summary>
    string Order(string token);

    /// <summary>The customer's deliveries ("My deliveries").</summary>
    string Deliveries();
}
