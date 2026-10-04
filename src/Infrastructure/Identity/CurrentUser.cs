using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Application.Abstractions;
using Application.Common;

namespace Infrastructure.Identity;

/// <summary>Reads the caller from the request's principal. Outside a request (jobs, seeding) everything is null.</summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public long? UserId => Read(ClaimTypes.NameIdentifier);

    public long? MerchantId => Read(AppClaims.MerchantId);

    private long? Read(string claimType)
    {
        var value = accessor.HttpContext?.User.FindFirstValue(claimType);

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
