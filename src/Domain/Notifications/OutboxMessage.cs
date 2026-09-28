using Domain.Common;

namespace Domain.Notifications;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum OutboxStatus : byte
{
    /// <summary>Waiting to be sent, now or at <see cref="OutboxMessage.NextAttemptOn"/>.</summary>
    Pending = 1,

    Sent = 2,

    /// <summary>Every attempt failed. Left for someone to look at; never retried by itself.</summary>
    Failed = 3
}

/// <summary>
/// A domain event waiting to be acted on (an SMS to the customer), saved in the same transaction as the change
/// that raised it, so a change is never lost without its message nor a message sent for a change rolled back.
/// The sender job handles it later and retries a failure with growing gaps: 1, 2, 4 and 8 minutes, then gives up.
/// <see cref="AuditedEntity.Created"/> is when the event happened.
/// </summary>
public class OutboxMessage : TenantEntity
{
    public const int MaxAttempts = 5;
    public const int MaxErrorLength = 1000;

    private OutboxMessage()
    {
    }

    /// <summary>The message contract's name, e.g. <c>OrderPlacedMessage</c>. Never a .NET type name.</summary>
    public string Type { get; private set; } = "";

    /// <summary>The message as JSON: ids only, read again when it is sent.</summary>
    public string Payload { get; private set; } = "";

    public OutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>Not before this moment (UTC). Null: as soon as possible.</summary>
    public DateTime? NextAttemptOn { get; private set; }

    public DateTime? SentOn { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(string type, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new OutboxMessage { Type = type, Payload = payload, Status = OutboxStatus.Pending };
    }

    public void MarkSent(DateTime now)
    {
        Attempts++;
        Status = OutboxStatus.Sent;
        SentOn = now;
        NextAttemptOn = null;
    }

    /// <summary>Records a failed attempt: waits longer after each one and gives up after <see cref="MaxAttempts"/>.</summary>
    public void MarkFailed(string error, DateTime now)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = OutboxStatus.Failed;
            NextAttemptOn = null;

            return;
        }

        NextAttemptOn = now.AddMinutes(Math.Pow(2, Attempts - 1));
    }
}
