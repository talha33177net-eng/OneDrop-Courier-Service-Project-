using Domain.Common;

namespace Domain.Parcels;

/// <summary>
/// One line of a parcel's tracking history: the status it was in and what happened, where. <see cref="AuditedEntity.Created"/>
/// is when, and <see cref="AuditedEntity.UpdatedId"/> who. Carries <see cref="MerchantId"/> so the merchant filter
/// needs no join.
/// </summary>
public class ParcelEvent : TenantEntity, IMerchantOwned
{
    private ParcelEvent()
    {
    }

    internal ParcelEvent(Parcel parcel, ParcelStatus status, string note, long? hubId)
    {
        Parcel = parcel;
        MerchantId = parcel.MerchantId;
        Status = status;
        Note = note;
        HubId = hubId;
    }

    public long ParcelId { get; private set; }

    public Parcel? Parcel { get; private set; }

    public long MerchantId { get; private set; }

    public ParcelStatus Status { get; private set; }

    public string Note { get; private set; } = "";

    /// <summary>The hub it happened at, when it happened at one.</summary>
    public long? HubId { get; private set; }
}

/// <summary>A parcel changed status: post it to its merchant's webhook and text the recipient when it matters.</summary>
public sealed record ParcelStatusChanged(Parcel Parcel, ParcelStatus Status) : IDomainEvent;
