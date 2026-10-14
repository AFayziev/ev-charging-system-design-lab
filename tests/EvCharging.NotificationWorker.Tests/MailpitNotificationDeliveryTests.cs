using System.Net;
using System.Net.Sockets;
using EvCharging.NotificationWorker.Configuration;
using EvCharging.NotificationWorker.Delivery;
using EvCharging.Persistence.Entities;
using Microsoft.Extensions.Options;
using Xunit;

namespace EvCharging.NotificationWorker.Tests;

public sealed class MailpitNotificationDeliveryTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, NotificationDeliveryOutcome.Uncertain)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, NotificationDeliveryOutcome.Failed)]
    public async Task Send_HttpFailure_DoesNotAssumeSafeRetry(
        HttpStatusCode status, NotificationDeliveryOutcome expected)
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        Assert.Equal(expected, (await Adapter(client).SendAsync(Notification(), Guid.NewGuid(), default)).Outcome);
    }

    [Fact]
    public async Task Send_TruncatedCaptureEvidence_IsUncertain()
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ID\":")
        }));
        Assert.Equal(
            NotificationDeliveryOutcome.Uncertain,
            (await Adapter(client).SendAsync(Notification(), Guid.NewGuid(), default)).Outcome);
    }

    [Theory]
    [InlineData(false, NotificationDeliveryOutcome.NotCaptured)]
    [InlineData(true, NotificationDeliveryOutcome.Uncertain)]
    public async Task Send_TransportFailure_RetrySafetyFollowsEvidence(
        bool responseLost, NotificationDeliveryOutcome expected)
    {
        var failure = responseLost
            ? new HttpRequestException(HttpRequestError.ResponseEnded)
            : new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused));
        using var client = Client((_, _) => throw failure);
        Assert.Equal(expected, (await Adapter(client).SendAsync(Notification(), Guid.NewGuid(), default)).Outcome);
    }

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send)) { BaseAddress = new Uri("http://mailpit.test") };

    private static MailpitNotificationDelivery Adapter(HttpClient client) => new(
        client,
        Options.Create(new NotificationDeliveryOptions { SenderEmail = "sender@example.test", RetryDelays = [] }));

    private static Notification Notification() => new()
    {
        PublicId = Guid.NewGuid(),
        RecipientEmail = "driver@example.test",
        Subject = "Reservation created",
        Body = "Frozen text",
        HtmlBody = "<p>Frozen text</p>",
        PreparedAt = DateTimeOffset.UtcNow
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
