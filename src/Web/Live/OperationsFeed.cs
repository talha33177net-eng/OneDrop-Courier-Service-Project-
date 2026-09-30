using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Application.Abstractions;

namespace Web.Live;

/// <summary>
/// Sends "changed" to an operator's dashboards at most once a second: a burst of saves (a van's worth of scans, Start
/// trip) is one message, sent a second after the first save. One app instance in the MVP; more instances would need a
/// SignalR backplane.
/// </summary>
public sealed class OperationsFeed(IHubContext<OperationsHub> hub, ILogger<OperationsFeed> logger) : IOperationsFeed
{
    private static readonly TimeSpan Gather = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<long, byte> pending = new();

    public void Changed(long tenantId)
    {
        if (pending.TryAdd(tenantId, 0))
        {
            _ = SendAsync(tenantId);
        }
    }

    private async Task SendAsync(long tenantId)
    {
        try
        {
            await Task.Delay(Gather);
            pending.TryRemove(tenantId, out _);
            await hub.Clients.Group(OperationsHub.GroupOf(tenantId)).SendAsync(OperationsHub.Changed);
        }
        catch (Exception exception)
        {
            pending.TryRemove(tenantId, out _);
            logger.LogWarning(exception, "Could not tell tenant {TenantId}'s dashboards about a change", tenantId);
        }
    }
}
