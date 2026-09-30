namespace Application.Abstractions;

/// <summary>
/// Tells the operator's open dashboards that its operations changed (orders, parcels, deliveries or trips), so they
/// read their counts again. Called after the change is committed; carries no data, so it can never leak any.
/// </summary>
public interface IOperationsFeed
{
    void Changed(long tenantId);
}
