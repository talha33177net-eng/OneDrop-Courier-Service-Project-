using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Notifications;

namespace Application.Notifications.SendEmails;

/// <summary>
/// Sends the tenant's due merchant emails (<see cref="MerchantEmails.Types"/>), oldest first, a batch per run, in a
/// loop of its own so a slow mail server never holds up a recipient's text or a shop's webhook. Each message is saved
/// as sent or failed on its own; a failure waits longer before each retry and is given up after
/// <see cref="OutboxMessage.MaxAttempts"/>.
/// </summary>
public class SendEmailsJob(IAppDbContext db, MerchantEmails emails, TimeProvider time, ILogger<SendEmailsJob> logger)
    : ITenantJob
{
    public const int BatchSize = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && (m.NextAttemptOn == null || m.NextAttemptOn <= now) &&
                MerchantEmails.Types.Contains(m.Type))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in due)
        {
            try
            {
                await emails.SendAsync(message, cancellationToken);
                message.MarkSent(now);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.MarkFailed(exception.Message, now);
                logger.LogWarning(
                    exception,
                    "Email {Id} ({Type}) failed, attempt {Attempts}",
                    message.Id,
                    message.Type,
                    message.Attempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
