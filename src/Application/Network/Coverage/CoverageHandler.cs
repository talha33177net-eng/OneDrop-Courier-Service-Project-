using Microsoft.EntityFrameworkCore;
using Application.Abstractions;

namespace Application.Network.Coverage;

public sealed record CoverageZone(string Code, string Name, string City, bool Suburb, IReadOnlyList<string> Areas);

public sealed record CoverageHub(string Code, string Name, string Address, string Phone, IReadOnlyList<CoverageZone> Zones);

/// <summary>The coverage map: every hub with the zones it serves and their areas.</summary>
public class CoverageHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<CoverageHub>> ListAsync(CancellationToken cancellationToken = default)
    {
        var areas = await (
            from area in db.Areas
            join zone in db.Zones on area.ZoneId equals zone.Id
            join hub in db.Hubs on zone.HubId equals hub.Id
            where !area.Archived && !zone.Archived && !hub.Archived
            select new
            {
                Hub = new { hub.Code, hub.Name, hub.Address, hub.Phone },
                Zone = new { zone.Code, zone.Name, zone.City, zone.IsSuburb },
                Area = area.Name
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. areas
                .GroupBy(a => a.Hub)
                .OrderBy(h => h.Key.Name)
                .Select(h => new CoverageHub(
                    h.Key.Code,
                    h.Key.Name,
                    h.Key.Address,
                    h.Key.Phone,
                    [
                        .. h.GroupBy(a => a.Zone)
                            .OrderBy(z => z.Key.Name)
                            .Select(z => new CoverageZone(
                                z.Key.Code,
                                z.Key.Name,
                                z.Key.City,
                                z.Key.IsSuburb,
                                [.. z.Select(a => a.Area).Order()]))
                    ]))
        ];
    }
}
