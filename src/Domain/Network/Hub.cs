using Domain.Common;

namespace Domain.Network;

/// <summary>
/// A branch where parcels are received, sorted, sent on to other hubs and handed to riders. Every zone is served by
/// one hub: the hub of a parcel's destination zone delivers it, the hub of its pickup point's zone collects it.
/// A hub the courier closes is archived, never deleted: the parcels that passed through it keep their history.
/// </summary>
public class Hub : TenantEntity, IArchivable
{
    private Hub()
    {
    }

    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public string Address { get; private set; } = "";

    public string Phone { get; private set; } = "";

    public bool Archived { get; private set; }

    public static Result<Hub> Create(string? code, string? name, string? address, string? phone)
    {
        var hub = new Hub();
        var changed = hub.Change(code, name, address, phone);

        return changed.IsSuccess ? hub : changed.Error!;
    }

    public Result Change(string? code, string? name, string? address, string? phone)
    {
        var shortCode = Codes.Read(code, "hub.code", "Give the hub a short code, such as MIR: letters and digits, at most 20.");
        if (shortCode.IsFailure)
        {
            return shortCode.Error!;
        }

        var trimmed = name.NullIfBlank();
        if (trimmed is null || trimmed.Length > 200)
        {
            return Error.Validation("hub.name", "Name the hub, such as \"Mirpur hub\", at most 200 characters.");
        }

        var where = address.NullIfBlank();
        if (where is null || where.Length > 500)
        {
            return Error.Validation("hub.address", "Enter the hub's address, at most 500 characters.");
        }

        var line = phone.NullIfBlank();
        if (line is null || line.Length > 20)
        {
            return Error.Validation("hub.phone", "Enter the hub's phone number, at most 20 characters.");
        }

        Code = shortCode.Value;
        Name = trimmed;
        Address = where;
        Phone = line;

        return Result.Success();
    }

    /// <summary>The hub closes: it serves no zone and takes no parcel, but its history stays.</summary>
    public void Archive()
    {
        Archived = true;
    }

    public void Restore()
    {
        Archived = false;
    }
}
