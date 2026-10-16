using EvCharging.NotificationWorker.Configuration;
using EvCharging.NotificationWorker.Delivery;
using EvCharging.NotificationWorker.Handlers;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Persistence.Extensions;
using EvCharging.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using Wolverine.Postgresql;
using Xunit;

namespace EvCharging.NotificationWorker.Tests;

public sealed class NotificationDeliveryTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    [Fact]
    public async Task Send_ConcurrentJobs_OnlyOneExternalCallIsAuthorized()
    {
        var notification = await PrepareAsync();
        var delivery = new StubDelivery(new(NotificationDeliveryOutcome.Captured, "captured-id"));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var database = CreateDatabase();
            try
            {
                await SendNotificationHandler.HandleAsync(new(notification.Id), database, delivery, _clock, default);
            }
            catch (DbUpdateException exception) when (exception.IsNotificationDeliveryConflict())
            {
                // Retry an authorization conflict with a fresh scope, as the Wolverine policy does.
                await using var retry = CreateDatabase();
                await SendNotificationHandler.HandleAsync(new(notification.Id), retry, delivery, _clock, default);
            }
        }));
        Assert.Equal(1, delivery.Calls);
        await using var storage = CreateDatabase();
        var saved = await storage.NotificationDeliveryAttempts.SingleAsync(value =>
            value.NotificationId == notification.Id);
        Assert.Equal(NotificationDeliveryOutcome.Captured, saved.Outcome);
        Assert.Equal("captured-id", saved.ExternalMessageId);
    }

    [Fact]
    public async Task Send_CancellationAfterAuthorization_RemainsUncertainAndUnrelatedSendContinues()
    {
        var notification = await PrepareAsync();
        await using (var database = CreateDatabase())
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => SendNotificationHandler.HandleAsync(
                new(notification.Id), database, new CancelledDelivery(), _clock, default));
        }

        var other = await PrepareAsync();
        var delivery = new StubDelivery(new(NotificationDeliveryOutcome.Captured, "other-id"));
        await using var restarted = CreateDatabase();
        await SendNotificationHandler.HandleAsync(new(notification.Id), restarted, delivery, _clock, default);
        await SendNotificationHandler.HandleAsync(new(other.Id), restarted, delivery, _clock, default);
        Assert.Equal(1, delivery.Calls);
        var interrupted = await restarted.NotificationDeliveryAttempts.AsNoTracking()
            .SingleAsync(value => value.NotificationId == notification.Id);
        Assert.Equal(NotificationDeliveryOutcome.Uncertain, interrupted.Outcome);
        Assert.Null(interrupted.CompletedAt);
    }

    [Theory]
    [InlineData(NotificationDeliveryOutcome.Uncertain)]
    [InlineData(NotificationDeliveryOutcome.Failed)]
    public async Task Send_UnsafeOrPermanentFailure_DoesNotAuthorizeAnotherSend(NotificationDeliveryOutcome outcome)
    {
        var notification = await PrepareAsync();
        var delivery = new StubDelivery(new(outcome, FailureCode: "test_failure"));
        await using var database = CreateDatabase();
        await SendNotificationHandler.HandleAsync(new(notification.Id), database, delivery, _clock, default);
        await SendNotificationHandler.HandleAsync(new(notification.Id), database, delivery, _clock, default);
        Assert.Equal(1, delivery.Calls);
        Assert.Equal(
            outcome,
            (await database.NotificationDeliveryAttempts.AsNoTracking()
                .SingleAsync(value => value.NotificationId == notification.Id)).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_TemporaryNonCapture_PreservesEachCallAndEndsInCaptureOrFailure(bool recovers)
    {
        var notification = await PrepareAsync();
        string failureCode = notification.Id.ToString("N");
        var delivery = new RecoveringDelivery(recovers, failureCode);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["NotificationDelivery:RetryDelays:0"] = "00:00:00.050";
        builder.Configuration["NotificationDelivery:RetryDelays:1"] = "00:00:00.050";
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<INotificationDelivery>(delivery);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddDbContext<PlatformDbContext>(
            options => options.UseNpgsql(fixture.GetDataSource("notification_messaging")));
        builder.UseWolverine(options =>
        {
            options.UseEntityFrameworkCoreTransactions();
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(SendNotificationHandler));
            options.CodeGeneration.AlwaysUseServiceLocationFor<INotificationDelivery>();
            options.PersistMessagesWithPostgresql(
                fixture.GetConnectionString("platform_migrator"), "notification_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.StubAllExternalTransports();
            options.Policies.UseDurableLocalQueues();
            options.PublishMessage<SendNotification>().ToLocalQueue("notification-delivery-test");
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(50);
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            await host.Services.GetRequiredService<IMessageBus>().SendAsync(new SendNotification(notification.Id));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var database = CreateDatabase();
            if (recovers)
            {
                await AwaitAsync(
                    () => database.NotificationDeliveryAttempts.AnyAsync(value =>
                        value.NotificationId == notification.Id
                            && value.Outcome == NotificationDeliveryOutcome.Captured),
                    timeout.Token);
            }
            else
            {
                var letters = host.Services.GetRequiredService<IMessageStore>().DeadLetters;
                var query = new DeadLetterEnvelopeQuery
                {
                    ExceptionMessage = $"Notification capture did not occur: {failureCode}."
                };
                await AwaitAsync(
                    async () => (await letters.QueryAsync(query, timeout.Token)).TotalCount == 1,
                    timeout.Token);
            }

            var attempts = await database.NotificationDeliveryAttempts.AsNoTracking()
                .Where(value => value.NotificationId == notification.Id)
                .OrderBy(value => value.Number)
                .ToArrayAsync();
            Assert.Equal(recovers ? 2 : 3, attempts.Length);
            Assert.Equal(attempts.Length, delivery.Calls);
            Assert.All(attempts, attempt => Assert.NotNull(attempt.CompletedAt));
            Assert.Equal(NotificationDeliveryOutcome.NotCaptured, attempts[0].Outcome);
            Assert.Equal(
                recovers ? NotificationDeliveryOutcome.Captured : NotificationDeliveryOutcome.NotCaptured,
                attempts[^1].Outcome);
        }
        catch (OperationCanceledException)
        {
            await using var storage = CreateDatabase();
            var attempts = await storage.NotificationDeliveryAttempts.AsNoTracking()
                .Where(value => value.NotificationId == notification.Id)
                .Select(value => value.Outcome)
                .ToArrayAsync();
            var errors = await host.Services.GetRequiredService<IMessageStore>().DeadLetters
                .QueryAsync(new DeadLetterEnvelopeQuery(), CancellationToken.None);
            string failures = string.Join(
                Environment.NewLine,
                errors.Envelopes.Select(error => error.ExceptionMessage));
            Assert.Fail($"Delivery calls: {delivery.Calls}; attempts: {string.Join(',', attempts)}; "
                + $"dead letters: {failures}");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private PlatformDbContext CreateDatabase()
    {
        return new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.GetDataSource("notification_messaging")).Options);
    }

    private async Task<Notification> PrepareAsync()
    {
        await using var database = CreateDatabase();
        var notification = new Notification
        {
            RecipientEmail = "driver@example.test",
            Subject = "Test notification",
            Body = "Frozen text",
            HtmlBody = "<p>Frozen text</p>",
            PreparedAt = _clock.GetUtcNow()
        };
        database.Notifications.Add(notification);
        await database.SaveChangesAsync();
        return notification;
    }

    private static async Task AwaitAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (!await condition())
        {
            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }

    private sealed class StubDelivery(NotificationDeliveryResult result) : INotificationDelivery
    {
        private int _calls;
        public int Calls => _calls;
        public Task<NotificationDeliveryResult> SendAsync(
            Notification notification, Guid attemptId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(result);
        }
    }

    private sealed class RecoveringDelivery(bool recovers, string failureCode) : INotificationDelivery
    {
        private int _calls;
        public int Calls => _calls;
        public Task<NotificationDeliveryResult> SendAsync(
            Notification notification, Guid attemptId, CancellationToken cancellationToken) => Task.FromResult(
                Interlocked.Increment(ref _calls) > 1 && recovers
                    ? new NotificationDeliveryResult(NotificationDeliveryOutcome.Captured, "captured-id")
                    : new NotificationDeliveryResult(
                        NotificationDeliveryOutcome.NotCaptured,
                        FailureCode: failureCode));
    }

    private sealed class CancelledDelivery : INotificationDelivery
    {
        public Task<NotificationDeliveryResult> SendAsync(
            Notification notification, Guid attemptId, CancellationToken cancellationToken) =>
            throw new OperationCanceledException();
    }
}
