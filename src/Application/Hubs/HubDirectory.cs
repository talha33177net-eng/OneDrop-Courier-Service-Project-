using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Network;

namespace Application.Hubs;

public sealed record HubItem(long Id, string Code, string Name, string Address, string Phone);

/// <summary>The courier's hubs, for pickers and for finding the hub a page is about. Another courier's hub is not found.</summary>
public class HubDirectory(IAppDbContext db)
{
    public async Task<IReadOnlyList<HubItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await db.Hubs
            .Where(h => !h.Archived)
            .OrderBy(h => h.Name)
            .Select(h => new HubItem(h.Id, h.Code, h.Name, h.Address, h.Phone))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Hub?> FindAsync(string? code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var upper = code.Trim().ToUpperInvariant();

        return await db.Hubs.SingleOrDefaultAsync(h => h.Code == upper && !h.Archived, cancellationToken);
    }
}
