using System.Globalization;
using System.Text;
using Application.Abstractions;

namespace Infrastructure.Webhooks;

/// <summary>
/// Posts a webhook with the Standard Webhooks headers. The client has a short timeout (<c>Webhooks:Timeout</c>) and
/// follows no redirects, so a shop's server cannot send our request somewhere else. The answer's body is never read.
/// </summary>
public class HttpWebhookSender(HttpClient client) : IWebhookSender
{
    public async Task<WebhookResponse> PostAsync(WebhookRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
        {
            Content = new StringContent(request.Body, Encoding.UTF8, "application/json")
        };
        message.Headers.Add("webhook-id", request.Id);
        message.Headers.Add("webhook-timestamp", request.Timestamp.ToString(CultureInfo.InvariantCulture));
        message.Headers.Add("webhook-signature", request.Signature);

        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return new WebhookResponse((int)response.StatusCode);
        }
        catch (HttpRequestException exception)
        {
            return new WebhookResponse(null, $"No answer: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookResponse(null, $"No answer within {client.Timeout.TotalSeconds:0} seconds");
        }
    }
}
