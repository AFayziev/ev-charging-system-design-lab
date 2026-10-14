using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wolverine;
using Wolverine.RabbitMQ;
using Xunit;

namespace EvCharging.E2E.Tests;

[Collection(nameof(E2ECollection))]
public sealed class ReservationNotificationTests(AppHostFixture fixture) : IAsyncLifetime
{
    private PlatformDbContext? _database;
    private IHost? _publisher;
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly List<Reservation> _reservations = [];
    private readonly ConcurrentQueue<Guid> _failures = new();
    private readonly ConcurrentQueue<Guid> _deliveryFailures = new();

    private PlatformDbContext Database =>
        _database ?? throw new InvalidOperationException("Storage is not initialized.");
    private IMessageBus Bus => (_publisher ?? throw new InvalidOperationException("Publisher is not initialized."))
        .Services
        .GetRequiredService<IMessageBus>();

    public async Task InitializeAsync()
    {
        _database = await fixture.CreatePlatformDbContextAsync();
        string? rabbit = await fixture.App.GetConnectionStringAsync("rabbitmq");
        Assert.NotNull(rabbit);
        var builder = Host.CreateApplicationBuilder();
        builder.UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ReservationCreated).Assembly;
            options.UseRabbitMq(rabbit).AutoProvision();
            options.PublishAllMessages().ToRabbitQueue("notification-reservation-events");
        });
        _publisher = builder.Build();
        await _publisher.StartAsync();
        _connection = await new ConnectionFactory { Uri = new Uri(rabbit) }.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        await _channel.QueueDeclarePassiveAsync("notification-send");
        var failures = new AsyncEventingBasicConsumer(_channel);
        failures.ReceivedAsync += (_, message) =>
        {
            using var document = JsonDocument.Parse(message.Body);
            if (document.RootElement.TryGetProperty("reservationId", out var reservationId))
            {
                _failures.Enqueue(reservationId.GetGuid());
            }

            if (document.RootElement.TryGetProperty("notificationId", out var notificationId))
            {
                _deliveryFailures.Enqueue(notificationId.GetGuid());
            }

            return Task.CompletedTask;
        };
        await _channel.BasicConsumeAsync("wolverine-dead-letter-queue", autoAck: true, consumer: failures);
    }

    public async Task DisposeAsync()
    {
        if (_publisher is not null)
        {
            await _publisher.StopAsync();
            _publisher.Dispose();
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        if (_database is not null)
        {
            try
            {
                foreach (var reservation in _reservations)
                {
                    var station = reservation.Connector!.Charger!.Station!;
                    var notificationIds = await _database.ReservationNotifications
                        .Where(value => value.ReservationId == reservation.Id)
                        .Select(value => value.NotificationId)
                        .ToArrayAsync();
                    await _database.ReservationNotifications.Where(value => value.ReservationId == reservation.Id)
                        .ExecuteDeleteAsync();
                    await _database.Notifications.Where(value => notificationIds.Contains(value.Id))
                        .ExecuteDeleteAsync();
                    await _database.Reservations.Where(value => value.Id == reservation.Id).ExecuteDeleteAsync();
                    await _database.Drivers.Where(value => value.Id == reservation.DriverId).ExecuteDeleteAsync();
                    await _database.Connectors.Where(value => value.Id == reservation.ConnectorId).ExecuteDeleteAsync();
                    await _database.Chargers.Where(value => value.Id == reservation.Connector.ChargerId)
                        .ExecuteDeleteAsync();
                    await _database.Stations.Where(value => value.Id == station.Id).ExecuteDeleteAsync();
                }
            }
            finally
            {
                await _database.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData(ReservationNotificationKind.Created)]
    [InlineData(ReservationNotificationKind.Cancelled)]
    [InlineData(ReservationNotificationKind.Expired)]
    public async Task Prepare_LifecycleFact_SnapshotsRecipientContentAndQueuesOneJob(ReservationNotificationKind kind)
    {
        var reservation = await BookAsync();
        var occurredAt = reservation.CreatedAt.AddMinutes(1);
        if (kind == ReservationNotificationKind.Expired)
        {
            occurredAt = reservation.EndsAt.AddMinutes(1);
            Assert.Equal(ReservationExpiryOutcome.Expired, reservation.ExpireAtSlotEnd(occurredAt, false));
        }
        else
        {
            // Creation must still be notified after a subsequent cancellation.
            Assert.True(reservation.Cancel(occurredAt, false).IsSuccess);
        }

        await Database.SaveChangesAsync();
        await Database.Drivers.Where(driver => driver.Id == reservation.DriverId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(driver => driver.Email, "current@example.test"));
        object message = kind switch
        {
            ReservationNotificationKind.Created => new ReservationCreated(reservation.Id, reservation.EndsAt),
            ReservationNotificationKind.Cancelled => new ReservationCancelled(reservation.Id),
            ReservationNotificationKind.Expired => new ReservationExpired(reservation.Id, occurredAt),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await Bus.InvokeAsync(message, timeout: TimeSpan.FromSeconds(45));
        var notification = await NotificationAsync(reservation.Id, kind);
        await AssertCapturedAsync(notification);
        Assert.Equal("current@example.test", notification.RecipientEmail);
        Assert.NotNull(notification.HtmlBody);
        Assert.Contains("EV Charging", notification.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(reservation.PublicId.ToString(), notification.HtmlBody, StringComparison.Ordinal);
        DateTimeOffset expectedTime = kind == ReservationNotificationKind.Created ? reservation.CreatedAt : occurredAt;
        Assert.NotEqual(Guid.Empty, notification.PublicId);
        Assert.NotEqual(notification.Id, notification.PublicId);
        Assert.Contains(reservation.PublicId.ToString(), notification.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(reservation.Id.ToString(), notification.Body, StringComparison.Ordinal);
        Assert.Contains("Station: Notification test", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Charger: Test charger", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Connector: 1", notification.Body, StringComparison.Ordinal);
        Assert.Contains(
            expectedTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC",
            notification.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("https://", notification.Body, StringComparison.Ordinal);

        await Database.Drivers.Where(driver => driver.Id == reservation.DriverId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(driver => driver.Email, "later@example.test"));
        await Database.Stations.Where(station => station.Id == reservation.Connector!.Charger!.StationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(station => station.Name, "Renamed station"));
        await Bus.InvokeAsync(message, timeout: TimeSpan.FromSeconds(45));
        var saved = await Database.Notifications.AsNoTracking().SingleAsync(value => value.Id == notification.Id);
        Assert.Equal(notification.RecipientEmail, saved.RecipientEmail);
        Assert.Equal(notification.Body, saved.Body);
        Assert.Equal(notification.HtmlBody, saved.HtmlBody);
        Assert.Equal(notification.PreparedAt, saved.PreparedAt);
        Assert.Equal(
            1,
            await Database.NotificationDeliveryAttempts.CountAsync(value =>
                value.NotificationId == notification.Id));
    }

    [Fact]
    public async Task Prepare_ConcurrentDuplicateFacts_ProducesOneNotificationAndJob()
    {
        var reservation = await BookAsync();
        int originalNotificationCount = await Database.Notifications.CountAsync();
        var message = new ReservationCreated(reservation.Id, reservation.EndsAt);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            Bus.InvokeAsync(message, timeout: TimeSpan.FromSeconds(45))));
        var notification = await NotificationAsync(reservation.Id, ReservationNotificationKind.Created);
        await AssertCapturedAsync(notification);
        Assert.Equal(
            1, await Database.ReservationNotifications.CountAsync(value => value.ReservationId == reservation.Id));
        Assert.Equal(originalNotificationCount + 1, await Database.Notifications.CountAsync());
        Assert.Equal(
            1,
            await Database.NotificationDeliveryAttempts.CountAsync(value =>
                value.NotificationId == notification.Id));
        Assert.DoesNotContain(reservation.Id, _failures);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Prepare_MissingReservationOrInvalidRecipient_RecordsFailureWithoutSendJob(bool missing)
    {
        var reservation = await BookAsync();
        Guid id = missing ? Guid.NewGuid() : reservation.Id;
        if (!missing)
        {
            await Database.Drivers.Where(driver => driver.Id == reservation.DriverId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(driver => driver.Email, "invalid"));
        }

        int originalNotificationCount = await Database.Notifications.CountAsync();
        await Bus.PublishAsync(new ReservationCreated(id, reservation.EndsAt));
        await AwaitAsync(() => Task.FromResult(_failures.Contains(id)));
        Assert.False(await Database.ReservationNotifications.AnyAsync(value => value.ReservationId == id));
        Assert.Equal(originalNotificationCount, await Database.Notifications.CountAsync());
        Assert.Equal(
            ReservationState.Active,
            await Database.Reservations
                .Where(value => value.Id == reservation.Id)
                .Select(value => value.State)
                .SingleAsync());
    }

    [Fact]
    public async Task Prepare_OutboxWriteFails_RollsBackNotificationAndAllowsLaterPreparation()
    {
        var reservation = await BookAsync();
        int originalNotificationCount = await Database.Notifications.CountAsync();
        await Database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION notification_messages.reject_test_send() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.destination = 'rabbitmq://queue/notification-send' THEN
                    RAISE EXCEPTION 'Injected send-job persistence failure';
                END IF;
                RETURN NEW;
            END; $$;
            CREATE TRIGGER reject_test_send BEFORE INSERT ON notification_messages.wolverine_outgoing_envelopes
                FOR EACH ROW EXECUTE FUNCTION notification_messages.reject_test_send();
            """);
        try
        {
            await Bus.PublishAsync(new ReservationCreated(reservation.Id, reservation.EndsAt));
            await AwaitAsync(() => Task.FromResult(_failures.Contains(reservation.Id)));
            Assert.False(
                await Database.ReservationNotifications.AnyAsync(value => value.ReservationId == reservation.Id));
            Assert.Equal(originalNotificationCount, await Database.Notifications.CountAsync());
        }
        finally
        {
            await Database.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_test_send ON notification_messages.wolverine_outgoing_envelopes;
                DROP FUNCTION notification_messages.reject_test_send();
                """);
        }

        await Bus.InvokeAsync(
            new ReservationCreated(reservation.Id, reservation.EndsAt), timeout: TimeSpan.FromSeconds(45));
        var notification = await NotificationAsync(reservation.Id, ReservationNotificationKind.Created);
        await AssertCapturedAsync(notification);
        Assert.Equal(
            1,
            await Database.NotificationDeliveryAttempts.CountAsync(value =>
                value.NotificationId == notification.Id));
    }

    [Fact]
    public async Task Deliver_OutcomeWriteFails_CaptureRemainsUncertainAndDuplicateFactDoesNotResend()
    {
        var reservation = await BookAsync();
        const string Recipient = "outcome-failure@example.test";
        await Database.Drivers.Where(driver => driver.Id == reservation.DriverId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(driver => driver.Email, Recipient));
        await Database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION charging.reject_test_notification_outcome() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.outcome = 'captured' AND EXISTS (
                    SELECT 1 FROM charging.notifications
                    WHERE id = NEW.notification_id AND recipient_email = 'outcome-failure@example.test') THEN
                    RAISE EXCEPTION 'Injected capture-outcome persistence failure';
                END IF;
                RETURN NEW;
            END; $$;
            CREATE TRIGGER reject_test_notification_outcome BEFORE UPDATE ON charging.notification_delivery_attempts
                FOR EACH ROW EXECUTE FUNCTION charging.reject_test_notification_outcome();
            """);
        try
        {
            var message = new ReservationCreated(reservation.Id, reservation.EndsAt);
            await Bus.InvokeAsync(message, timeout: TimeSpan.FromSeconds(45));
            var notification = await NotificationAsync(reservation.Id, ReservationNotificationKind.Created);
            await AwaitAsync(() => Database.NotificationDeliveryAttempts.AnyAsync(value =>
                value.NotificationId == notification.Id));
            var attempt = await Database.NotificationDeliveryAttempts.AsNoTracking()
                .SingleAsync(value => value.NotificationId == notification.Id);
            await AwaitAsync(() => Task.FromResult(_deliveryFailures.Contains(notification.Id)));
            Assert.Equal(NotificationDeliveryOutcome.Uncertain, attempt.Outcome);
            Assert.Null(attempt.CompletedAt);
            using var mailbox = fixture.App.CreateHttpClient("notification-mail", "http");
            var captured = await mailbox.GetFromJsonAsync<CapturedEmails>(
                "/api/v1/search?query=" + Uri.EscapeDataString("to:" + Recipient));
            Assert.NotNull(captured);
            Assert.Single(captured.Messages);
            await Bus.InvokeAsync(message, timeout: TimeSpan.FromSeconds(45));
            Assert.Equal(
                1,
                await Database.NotificationDeliveryAttempts.CountAsync(value =>
                    value.NotificationId == notification.Id));
            Assert.Equal(
                ReservationState.Active,
                await Database.Reservations.AsNoTracking()
                    .Where(value => value.Id == reservation.Id)
                    .Select(value => value.State)
                    .SingleAsync());

            var unrelated = await BookAsync();
            await Bus.InvokeAsync(
                new ReservationCreated(unrelated.Id, unrelated.EndsAt), timeout: TimeSpan.FromSeconds(45));
            await AssertCapturedAsync(await NotificationAsync(unrelated.Id, ReservationNotificationKind.Created));
        }
        finally
        {
            await Database.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_test_notification_outcome ON charging.notification_delivery_attempts;
                DROP FUNCTION charging.reject_test_notification_outcome();
                """);
        }
    }

    private sealed record CapturedEmails(CapturedEmailSummary[] Messages);
    private sealed record CapturedEmailSummary(string ID);

    private async Task AssertCapturedAsync(Notification notification)
    {
        await AwaitAsync(() => Database.NotificationDeliveryAttempts.AnyAsync(value =>
            value.NotificationId == notification.Id && value.Outcome == NotificationDeliveryOutcome.Captured));
        var attempt = await Database.NotificationDeliveryAttempts.AsNoTracking()
            .SingleAsync(value => value.NotificationId == notification.Id);
        Assert.NotNull(attempt.CompletedAt);
        Assert.NotNull(attempt.ExternalMessageId);
        using var mailbox = fixture.App.CreateHttpClient("notification-mail", "http");
        var captured = await mailbox.GetFromJsonAsync<CapturedEmail>("/api/v1/message/" + attempt.ExternalMessageId);
        Assert.NotNull(captured);
        Assert.Equal(notification.Subject, captured.Subject);
        Assert.Equal(
            notification.Body.Replace("\r\n", "\n", StringComparison.Ordinal),
            captured.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(
            notification.HtmlBody?.Replace("\r\n", "\n", StringComparison.Ordinal),
            captured.HTML.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(notification.RecipientEmail, Assert.Single(captured.To).Address);
    }

    private sealed record CapturedEmail(string Subject, string Text, string HTML, CapturedAddress[] To);
    private sealed record CapturedAddress(string Address);

    private async Task<Reservation> BookAsync()
    {
        var station = new Station
        {
            Name = "Notification test",
            Address = "Synthetic address",
            TimeZoneId = "Asia/Tashkent",
            AccessInstructions = "Test"
        };
        var charger = new Charger { Station = station, Name = "Test charger" };
        var connector = new Connector
        {
            Charger = charger,
            Number = 1,
            ConnectorType = "Type2",
            PowerSupply = "AC",
            RatedPowerKW = 22
        };
        var driver = Driver.Register(
            "https://notification.test", Guid.NewGuid().ToString(), EmailAddress.Create("original@example.test").Value);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var reservation = Reservation.Book(
            connector, driver, now.AddHours(1), now.AddHours(2), now, AppHostFixture.BookingLimits).Value;
        Database.Reservations.Add(reservation);
        await Database.SaveChangesAsync();
        _reservations.Add(reservation);
        return reservation;
    }

    private Task<Notification> NotificationAsync(Guid reservationId, ReservationNotificationKind kind) =>
        Database.ReservationNotifications.AsNoTracking()
            .Where(value => value.ReservationId == reservationId && value.Kind == kind)
            .Join(
                Database.Notifications,
                value => value.NotificationId,
                value => value.Id,
                (_, notification) => notification)
            .SingleAsync();

    private static async Task AwaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }
}
