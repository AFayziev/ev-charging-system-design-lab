using System.Net.Http.Json;
using System.Text.Json;
using EvCharging.NotificationWorker.Configuration;
using EvCharging.Persistence.Entities;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace EvCharging.NotificationWorker.Delivery;

internal sealed class MailpitNotificationDelivery(
    HttpClient client,
    IOptions<NotificationDeliveryOptions> options) : INotificationDelivery
{
    public string Provider => NotificationDeliveryProvider.Mailpit.ToString();

    public async Task<NotificationDeliveryResult> SendAsync(
        Notification notification, Guid attemptId, CancellationToken cancellationToken)
    {
        var request = new SendRequest(
            new Address(options.Value.SenderEmail, options.Value.SenderName),
            [new Address(notification.RecipientEmail)],
            notification.Subject,
            notification.Body,
            notification.HtmlBody,
            ["notification:" + notification.PublicId, "attempt:" + attemptId]);
        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/send", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Other error responses alone do not prove that capture did not happen.
                var outcome = response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge
                    ? NotificationDeliveryOutcome.Failed
                    : NotificationDeliveryOutcome.Uncertain;
                return new(outcome, FailureCode: NotificationFailureCodes.Transport.Http((int)response.StatusCode));
            }

            var captured = await response.Content.ReadFromJsonAsync<SendResponse>(cancellationToken);
            return string.IsNullOrWhiteSpace(captured?.ID) || captured.ID.Length > 200
                ? new(
                    NotificationDeliveryOutcome.Uncertain,
                    FailureCode: NotificationFailureCodes.Mailpit.InvalidCaptureResponse)
                : new(NotificationDeliveryOutcome.Captured, captured.ID);
        }
        catch (HttpRequestException exception) when (exception.IsConnectionNotEstablished())
        {
            return new(
                NotificationDeliveryOutcome.NotCaptured,
                FailureCode: NotificationFailureCodes.Transport.ConnectionNotEstablished);
        }
        catch (HttpRequestException)
        {
            return new(
                NotificationDeliveryOutcome.Uncertain,
                FailureCode: NotificationFailureCodes.Transport.TransportFailure);
        }
        catch (JsonException)
        {
            return new(
                NotificationDeliveryOutcome.Uncertain,
                FailureCode: NotificationFailureCodes.Mailpit.InvalidCaptureResponse);
        }
        catch (IOException)
        {
            return new(
                NotificationDeliveryOutcome.Uncertain,
                FailureCode: NotificationFailureCodes.Transport.TransportFailure);
        }
        catch (TimeoutRejectedException)
        {
            return new(
                NotificationDeliveryOutcome.Uncertain,
                FailureCode: NotificationFailureCodes.Transport.SendTimeout);
        }
        catch (BrokenCircuitException)
        {
            return new(
                NotificationDeliveryOutcome.NotCaptured,
                FailureCode: NotificationFailureCodes.Transport.CircuitOpen);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(
                NotificationDeliveryOutcome.Uncertain,
                FailureCode: NotificationFailureCodes.Transport.SendTimeout);
        }
    }

    private sealed record Address(string Email, string? Name = null);
    private sealed record SendRequest(
        Address From,
        Address[] To,
        string Subject,
        string Text,
        string? HTML,
        string[] Tags);
    private sealed record SendResponse(string ID);
}
