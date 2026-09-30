using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Application.Common;
using Web.Authentication;

namespace Web.Live;

/// <summary>
/// The live line to the operator's dashboards. A connection joins its operator's group only (from the sign-in's tenant
/// claim, which the tenant guard has already matched to the subdomain) and hears one message, "changed", with no data:
/// the page then reads its counts again through its own authorised request.
/// </summary>
[Authorize(Policy = Policies.Operations)]
public sealed class OperationsHub : Hub
{
    public const string Path = "/hubs/operations";

    public const string Changed = "changed";

    public static string GroupOf(long tenantId)
    {
        return $"tenant-{tenantId}";
    }

    public override async Task OnConnectedAsync()
    {
        var tenantId = long.Parse(Context.User!.FindFirstValue(AppClaims.TenantId)!);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(tenantId));
        await base.OnConnectedAsync();
    }
}
