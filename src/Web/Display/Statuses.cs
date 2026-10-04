using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Html;
using Application.Payments.MerchantPayments;
using Domain.Common;
using Domain.Delivery;
using Domain.Merchants;
using Domain.Parcels;
using Domain.Payments;

namespace Web.Display;

/// <summary>
/// The words and colours every page uses for statuses, so a merchant, hub staff, a rider and an admin read the same thing
/// for the same status instead of the enum's name.
/// </summary>
public static class Statuses
{
    public static IHtmlContent Badge(ParcelStatus status)
    {
        var tone = status switch
        {
            ParcelStatus.Pending => "pending",
            ParcelStatus.PickedUp or ParcelStatus.AtHub or ParcelStatus.InTransit => "transit",
            ParcelStatus.OutForDelivery => "out",
            ParcelStatus.OnHold => "hold",
            ParcelStatus.Delivered or ParcelStatus.PartlyDelivered => "done",
            ParcelStatus.Returning or ParcelStatus.Returned => "return",
            _ => "cancel"
        };

        return Pill(tone, status.DisplayName());
    }

    public static IHtmlContent Badge(MerchantStatus status)
    {
        return status switch
        {
            MerchantStatus.Pending => Pill("pending", "Awaiting approval"),
            MerchantStatus.Active => Pill("done", "Active"),
            _ => Pill("return", "Suspended")
        };
    }

    public static IHtmlContent Badge(PickupStatus status)
    {
        return status switch
        {
            PickupStatus.Requested => Pill("pending", "Requested"),
            PickupStatus.Assigned => Pill("out", "Rider assigned"),
            PickupStatus.Completed => Pill("done", "Picked up"),
            _ => Pill("cancel", "Cancelled")
        };
    }

    public static IHtmlContent Badge(RunStatus status)
    {
        return status == RunStatus.Open ? Pill("out", "Open") : Pill("done", "Closed");
    }

    public static IHtmlContent Badge(PayoutStatus status)
    {
        return status == PayoutStatus.Paid ? Pill("done", "Paid") : Pill("pending", "Processing");
    }

    public static IHtmlContent Badge(AttemptOutcome? outcome)
    {
        return outcome switch
        {
            AttemptOutcome.Delivered => Pill("done", "Delivered"),
            AttemptOutcome.PartlyDelivered => Pill("done", "Partly delivered"),
            AttemptOutcome.Hold => Pill("hold", "On hold"),
            AttemptOutcome.Refused => Pill("return", "Refused"),
            _ => Pill("out", "With rider")
        };
    }

    public static string Name(LedgerEntryKind kind)
    {
        return kind.DisplayName();
    }

    private static HtmlString Pill(string tone, string text)
    {
        return new HtmlString($"""<span class="badge badge-{tone}">{WebUtility.HtmlEncode(text)}</span>""");
    }
}

/// <summary>
/// Taka amounts and phone numbers as people read them. Returned as HTML so the ৳ sign is written as it is; Razor would
/// otherwise encode it inside a C# expression.
/// </summary>
public static class Money
{
    public static IHtmlContent Taka(decimal amount)
    {
        var text = amount < 0
            ? "−৳" + (-amount).ToString("N0", CultureInfo.InvariantCulture)
            : "৳" + amount.ToString("N0", CultureInfo.InvariantCulture);

        return new HtmlString($"""<span class="amount">{text}</span>""");
    }

    public static IHtmlContent Taka(decimal? amount)
    {
        return amount is { } value ? Taka(value) : new HtmlString("""<span class="faint">—</span>""");
    }

    /// <summary>A stored E.164 number in the local form people dial: 01712345678.</summary>
    public static string Phone(string? e164)
    {
        return PhoneNumber.Parse(e164) is { IsSuccess: true } parsed ? parsed.Value.Local : e164 ?? "";
    }

    public static string Kg(int grams)
    {
        return (grams / 1000m).ToString("0.##", CultureInfo.InvariantCulture) + " kg";
    }
}
