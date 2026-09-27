using Microsoft.EntityFrameworkCore;
using Application.Abstractions;

namespace Application.Network.ListAreas;

public sealed record AreaItem(long Id, string Name, string Zone, string Hub);

/// <summary>The area list merchants show at checkout. An address names one of these instead of free-text geocoding.</summary>
public class ListAreasHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<AreaItem>> HandleAsync(CancellationToken cancellationToken = default)
    {
        return await db.Areas
            .Where(a => !a.Archived)
            .OrderBy(a => a.Zone!.Name)
            .ThenBy(a => a.Name)
            .Select(a => new AreaItem(a.Id, a.Name, a.Zone!.Name, a.Zone.Hub!.Name))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
