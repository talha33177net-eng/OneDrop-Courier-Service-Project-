namespace Application.Abstractions;

/// <summary>
/// One email to one person. <paramref name="Html"/> is what most people see; <paramref name="Text"/> is the same
/// message in plain words, for a reader that shows no HTML.
/// </summary>
public sealed record EmailMessage(string To, string? ToName, string Subject, string Html, string Text);

/// <summary>
/// Adapter for the mail server. The courier's own address and name come from configuration (`Email:*`); what is in
/// the message is the courier's business. Development without a mail server uses a fake that lists them at
/// <c>/Dev/Emails</c>.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
