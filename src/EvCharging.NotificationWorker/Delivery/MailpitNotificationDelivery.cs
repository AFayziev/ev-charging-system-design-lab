using System.Net.Http.Json;
using System.Net.Sockets;
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
    public async Task<NotificationDeliveryResult> SendAsync(
        Notification notification, Guid attemptId, CancellationToken cancellationToken)
    {
        var request = new SendRequest(
            new Address(options.Value.SenderEmail),
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
                if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
                {
                    return new(NotificationDeliveryOutcome.Failed, FailureCode: "message_too_large");
                }

                // Error responses alone do not prove that capture did not happen.
                return new(NotificationDeliveryOutcome.Uncertain, FailureCode: "http_" + (int)response.StatusCode);
            }

            var captured = await response.Content.ReadFromJsonAsync<SendResponse>(cancellationToken);
            return string.IsNullOrWhiteSpace(captured?.ID) || captured.ID.Length > 200
                ? new(NotificationDeliveryOutcome.Uncertain, FailureCode: "invalid_capture_response")
                : new(NotificationDeliveryOutcome.Captured, captured.ID);
        }
        catch (HttpRequestException exception) when (
            exception.HttpRequestError == HttpRequestError.NameResolutionError
            || exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
        {
            return new(NotificationDeliveryOutcome.NotCaptured, FailureCode: "connection_not_established");
        }
        catch (HttpRequestException)
        {
            return new(NotificationDeliveryOutcome.Uncertain, FailureCode: "transport_failure");
        }
        catch (JsonException)
        {
            return new(NotificationDeliveryOutcome.Uncertain, FailureCode: "invalid_capture_response");
        }
        catch (IOException)
        {
            return new(NotificationDeliveryOutcome.Uncertain, FailureCode: "response_read_failure");
        }
        catch (TimeoutRejectedException)
        {
            return new(NotificationDeliveryOutcome.Uncertain, FailureCode: "send_timeout");
        }
        catch (BrokenCircuitException)
        {
            return new(NotificationDeliveryOutcome.NotCaptured, FailureCode: "circuit_open");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(NotificationDeliveryOutcome.Uncertain, FailureCode: "send_timeout");
        }
    }

    private sealed record Address(string Email);
    private sealed record SendRequest(
        Address From,
        Address[] To,
        string Subject,
        string Text,
        string? HTML,
        string[] Tags);
    private sealed record SendResponse(string ID);
}
