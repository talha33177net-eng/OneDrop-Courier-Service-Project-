using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Notifications;

namespace Application.Notifications.SendOutbox;

/// <summary>
/// Texts the customers the tenant's due outbox messages (<see cref="CustomerTexts.Types"/>), oldest first, a batch
/// per run; it runs every few seconds. Each message is saved as sent or failed on its own, so one failure never
/// resends the others. A failure waits longer before each
/// retry and is given up after <see cref="OutboxMessage.MaxAttempts"/>.
/// </summary>
public class SendOutboxJob(IAppDbContext db, CustomerTexts texts, TimeProvider time, ILogger<SendOutboxJob> logger)
    : ITenantJob
{
    public const int BatchSize = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && (m.NextAttemptOn == null || m.NextAttemptOn <= now) &&
                CustomerTexts.Types.Contains(m.Type))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in due)
        {
            try
            {
                await texts.SendAsync(message, cancellationToken);
                message.MarkSent(now);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.MarkFailed(exception.Message, now);
                logger.LogWarning(
                    exception,
                    "Outbox message {Id} ({Type}) failed, attempt {Attempts}",
                    message.Id,
                    message.Type,
                    message.Attempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
