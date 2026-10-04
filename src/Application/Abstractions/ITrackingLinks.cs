namespace Application.Abstractions;

/// <summary>
/// Addresses of the public tracking page, for links in SMS. Built from a configured format per tenant subdomain, not
/// from the current request: background work (the outbox) has none.
/// </summary>
public interface ITrackingLinks
{
    string Track(string trackingCode);
}
