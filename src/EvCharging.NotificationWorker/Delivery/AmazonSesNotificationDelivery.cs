using System.Net;
using System.Net.Mail;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using EvCharging.NotificationWorker.Configuration;
using EvCharging.Persistence.Entities;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace EvCharging.NotificationWorker.Delivery;

internal sealed partial class AmazonSesNotificationDelivery(
    IAmazonSimpleEmailServiceV2 client,
    IOptions<NotificationDeliveryOptions> options,
    ILogger<AmazonSesNotificationDelivery> logger) : INotificationDelivery
{
    private readonly string _from = new MailAddress(options.Value.SenderEmail, options.Value.SenderName).ToString();

    public string Provider => NotificationDeliveryProvider.AmazonSes.ToString();

    public async Task<NotificationDeliveryResult> SendAsync(
        Notification notification, Guid attemptId, CancellationToken cancellationToken)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = _from,
            Destination = new Destination { ToAddresses = [notification.RecipientEmail] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = notification.Subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Text = new Content { Data = notification.Body, Charset = "UTF-8" },
                        Html = notification.HtmlBody is null
                            ? null
                            : new Content { Data = notification.HtmlBody, Charset = "UTF-8" }
                    }
                }
            },
            EmailTags =
            [
                new MessageTag { Name = "notification", Value = notification.PublicId.ToString() },
                new MessageTag { Name = "attempt", Value = attemptId.ToString() }
            ]
        };
        try
        {
            var response = await client.SendEmailAsync(request, cancellationToken);
            return string.IsNullOrWhiteSpace(response.MessageId) || response.MessageId.Length > 200
                ? new(
                    NotificationDeliveryOutcome.Uncertain,
                    FailureCode: NotificationFailureCodes.AmazonSes.InvalidAcceptanceResponse)
                : new(NotificationDeliveryOutcome.Captured, response.MessageId);
        }
        catch (AmazonSimpleEmailServiceV2Exception exception) when (Rejection(exception) is { } code)
        {
            return Fail(exception, NotificationDeliveryOutcome.Failed, code, notification.PublicId, attemptId);
        }
        catch (AmazonServiceException exception) when (IsThrottle(exception))
        {
            // Throttled requests are rejected before processing, so a later retry cannot duplicate the email.
            return Fail(
                exception,
                NotificationDeliveryOutcome.NotCaptured,
                NotificationFailureCodes.AmazonSes.Throttled,
                notification.PublicId,
                attemptId);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && NotSent(exception) is { } code)
        {
            return new(NotificationDeliveryOutcome.NotCaptured, FailureCode: code);
        }
        catch (AmazonServiceException exception) when (exception.StatusCode != 0)
        {
            // A refused request (for example 403 for credentials or permissions) cannot succeed on repeat until
            // it or the configuration changes. Other errors do not prove the message was not accepted.
            var outcome = IsClientError(exception)
                ? NotificationDeliveryOutcome.Failed
                : NotificationDeliveryOutcome.Uncertain;
            return Fail(
                exception,
                outcome,
                NotificationFailureCodes.Transport.Http((int)exception.StatusCode),
                notification.PublicId,
                attemptId);
        }
        catch (Exception exception) when (exception is AmazonClientException or HttpRequestException
            or TimeoutRejectedException || exception is OperationCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            // The effect may have happened before the response was lost or timed out.
            var timedOut = exception.InnerExceptions()
                .Any(inner => inner is TimeoutRejectedException or OperationCanceledException);
            var code = timedOut
                ? NotificationFailureCodes.Transport.SendTimeout
                : NotificationFailureCodes.Transport.TransportFailure;
            LogTransportError(exception, notification.PublicId, attemptId, code);
            return new(NotificationDeliveryOutcome.Uncertain, FailureCode: code);
        }
    }

    [LoggerMessage(
        LogLevel.Warning,
        "SES failed notification {NotificationId} attempt {AttemptId} as {FailureCode} "
        + "(AWS error {ErrorCode}, request {RequestId})")]
    private partial void LogServiceError(
        Exception exception,
        Guid notificationId,
        Guid attemptId,
        string failureCode,
        string? errorCode,
        string? requestId);

    [LoggerMessage(
        LogLevel.Warning,
        "SES send of notification {NotificationId} attempt {AttemptId} ended without a response: {FailureCode}")]
    private partial void LogTransportError(
        Exception exception,
        Guid notificationId,
        Guid attemptId,
        string failureCode);

    private NotificationDeliveryResult Fail(
        AmazonServiceException exception,
        NotificationDeliveryOutcome outcome,
        string code,
        Guid notificationId,
        Guid attemptId)
    {
        LogServiceError(exception, notificationId, attemptId, code, exception.ErrorCode, exception.RequestId);
        return new(outcome, FailureCode: code);
    }

    // 408 is a timeout, which does not prove the request was refused. Throttling (429) is handled before this.
    private static bool IsClientError(AmazonServiceException exception) =>
        exception.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            and not HttpStatusCode.RequestTimeout;

    private static bool IsThrottle(AmazonServiceException exception) =>
        exception is TooManyRequestsException
        || exception.StatusCode == HttpStatusCode.TooManyRequests
        || exception.ErrorCode == "ThrottlingException";

    // Permanent rejections that share HTTP 400, so the status alone cannot tell them apart. LimitExceeded is a
    // resource-count limit, not a transient rate. Other 4xx errors are recorded by status code.
    private static string? Rejection(AmazonSimpleEmailServiceV2Exception exception) => exception switch
    {
        MessageRejectedException => NotificationFailureCodes.AmazonSes.MessageRejected,
        MailFromDomainNotVerifiedException => NotificationFailureCodes.AmazonSes.SenderDomainNotVerified,
        AccountSuspendedException => NotificationFailureCodes.AmazonSes.AccountSuspended,
        SendingPausedException => NotificationFailureCodes.AmazonSes.SendingPaused,
        LimitExceededException => NotificationFailureCodes.AmazonSes.LimitExceeded,
        _ => null
    };

    // Evidence that the request never reached SES.
    private static string? NotSent(Exception exception)
    {
        foreach (var inner in exception.InnerExceptions())
        {
            switch (inner)
            {
                case BrokenCircuitException:
                    return NotificationFailureCodes.Transport.CircuitOpen;
                case HttpRequestException request when request.IsConnectionNotEstablished():
                    return NotificationFailureCodes.Transport.ConnectionNotEstablished;
            }
        }

        return null;
    }
}

file static class ExceptionChainExtensions
{
    public static IEnumerable<Exception> InnerExceptions(this Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
