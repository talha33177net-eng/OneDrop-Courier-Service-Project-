using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Customers;

namespace Infrastructure.Sms;

public sealed record SentSms(DateTime On, string To, string Sender, string Text);

/// <summary>Recent messages the fake sender "sent", so a demo can read an OTP without a phone.</summary>
public class SmsLog
{
    private const int Keep = 50;
    private readonly ConcurrentQueue<SentSms> messages = new();

    public IReadOnlyList<SentSms> Recent => [.. messages.Reverse()];

    public void Add(SentSms message)
    {
        messages.Enqueue(message);
        while (messages.Count > Keep)
        {
            messages.TryDequeue(out _);
        }
    }
}

/// <summary>Adapter stand-in for the SMS gateway: logs the message and keeps it in <see cref="SmsLog"/>.</summary>
public class FakeSmsSender(SmsLog log, TimeProvider time, ILogger<FakeSmsSender> logger) : ISmsSender
{
    public Task SendAsync(PhoneNumber to, string senderName, string text, CancellationToken cancellationToken = default)
    {
        log.Add(new SentSms(time.GetUtcNow().UtcDateTime, to.Value, senderName, text));
        logger.LogInformation("SMS from {Sender} to {Phone}: {Text}", senderName, to.Value, text);

        return Task.CompletedTask;
    }
}
