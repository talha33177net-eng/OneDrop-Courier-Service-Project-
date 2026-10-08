using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Notifications;
using Domain.Payments;

namespace Application.Notifications.SendEmails;

/// <summary>
/// Writes and sends a merchant's email for an outbox message, from the data as it is when sent, like the recipients'
/// texts. Today there is one: the payout that has left for the merchant's account, with what it is made of and a link
/// to the invoice. A merchant with no email address anywhere is skipped rather than retried for ever.
/// </summary>
public class MerchantEmails(
    IAppDbContext db,
    ITenantContext tenantContext,
    IEmailSender email,
    IUserAccounts accounts,
    ITrackingLinks links)
{
    /// <summary>The outbox message types these emails handle; the email job takes only these.</summary>
    public static readonly string[] Types = [nameof(MerchantEmailMessage)];

    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Emails need a tenant.");
        var payload = JsonSerializer.Deserialize<MerchantEmailMessage>(message.Payload)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has no payload.");

        var merchant = await db.Merchants
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(m => m.Id == payload.MerchantId)
            .Select(m => new { m.Id, m.Name, m.ContactEmail, m.MainMerchantId })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Merchant {payload.MerchantId} of outbox message {message.Id} is gone.");

        var to = merchant.ContactEmail
            ?? await accounts.MerchantEmailAsync(merchant.MainMerchantId ?? merchant.Id, cancellationToken);
        if (to is null)
        {
            // Nowhere to write to: the merchant gave no email and its account has no login left
            return;
        }

        var written = payload.Kind switch
        {
            MerchantEmailKind.PayoutSent => await PayoutSentAsync(payload.SubjectId, merchant.Name, tenant.Name, tenant.CurrencyCode, cancellationToken),
            _ => null
        };
        if (written is null)
        {
            return;
        }

        await email.SendAsync(written with { To = to, ToName = merchant.Name }, cancellationToken);
    }

    private async Task<EmailMessage?> PayoutSentAsync(
        long payoutId,
        string merchant,
        string courier,
        string currency,
        CancellationToken cancellationToken)
    {
        var payout = await db.Payouts
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(p => p.Id == payoutId && p.Status == PayoutStatus.Paid)
            .Select(p => new { p.Number, p.Amount, p.CodTotal, p.ChargesTotal, p.AdjustmentsTotal, p.Method, p.Account, p.UpToDate, p.PaidOn })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (payout is null)
        {
            // The payout is gone or was never confirmed; there is nothing to tell the merchant
            return null;
        }

        var account = payout.Account.Length > 4 ? $"…{payout.Account[^4..]}" : payout.Account;
        var subject = $"{courier}: {Money(payout.Amount, currency)} sent to you ({payout.Number})";
        (string Label, string Value)[] lines =
        [
            ("Cash collected for you", Money(payout.CodTotal, currency)),
            ("Our charges", $"−{Money(payout.ChargesTotal, currency)}"),
            .. payout.AdjustmentsTotal == 0
                ? Array.Empty<(string, string)>()
                : [("Adjustments", payout.AdjustmentsTotal < 0 ? $"−{Money(-payout.AdjustmentsTotal, currency)}" : Money(payout.AdjustmentsTotal, currency))],
            ("Sent to you", Money(payout.Amount, currency)),
            ("To your account", $"{payout.Method} {account}"),
            ("For parcels up to", payout.UpToDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture))
        ];

        var text = $"Hello {merchant},\r\n\r\n{Money(payout.Amount, currency)} is on its way to your {payout.Method} account {account}.\r\n\r\n"
            + string.Join("\r\n", lines.Select(line => $"{line.Label}: {line.Value}"))
            + $"\r\n\r\nInvoice {payout.Number}: {links.Invoice(payout.Number)}\r\n\r\n{courier}";

        var rows = string.Join("", lines.Select(line =>
            $"""<tr><td style="padding:6px 0;color:#5b6472">{line.Label}</td><td style="padding:6px 0;text-align:right;font-weight:600">{line.Value}</td></tr>"""));
        var html = $"""
            <div style="font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#141820;max-width:520px">
              <h2 style="margin:0 0 4px">{Money(payout.Amount, currency)} is on its way</h2>
              <p style="margin:0 0 18px;color:#5b6472">Hello {merchant}, we have sent your payout to your {payout.Method} account {account}.</p>
              <table style="width:100%;border-collapse:collapse;font-size:15px">{rows}</table>
              <p style="margin:20px 0 0"><a href="{links.Invoice(payout.Number)}" style="color:#0b6a9e">See invoice {payout.Number}</a></p>
              <p style="margin:18px 0 0;color:#5b6472;font-size:13px">{courier}</p>
            </div>
            """;

        return new EmailMessage("", null, subject, html, text);
    }

    private static string Money(decimal amount, string currency)
    {
        return currency == "BDT"
            ? $"৳{amount.ToString("N0", CultureInfo.InvariantCulture)}"
            : $"{amount.ToString("N0", CultureInfo.InvariantCulture)} {currency}";
    }
}
