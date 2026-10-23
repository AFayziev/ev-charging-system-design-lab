using System.Collections.Concurrent;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Persistence;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Extensions;
using EvCharging.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.Tracking;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

public sealed class ReservationExpiryTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>,
    IAsyncLifetime
{
    private static readonly DateTimeOffset SlotStart = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PastDeadline = SlotStart.AddMinutes(11);
    private IHost? _messaging;

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(PastDeadline));
        builder.Services.AddScoped<IAuditActorAccessor, WorkerAuditActorAccessor>();
        builder.Services.AddScoped<AuditEntryWriter>();
        builder.Services.AddScoped<AuditSaveChangesInterceptor>();
        builder.Services.AddDbContext<PlatformDbContext>((services, options) =>
            options.UseNpgsql(fixture.GetDataSource("charging_messaging"))
                .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));
        builder.Services.AddSingleton<ExpiryRecorder>();
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.UseEntityFrameworkCoreTransactions();
            options.PersistMessagesWithPostgresql(
                fixture.GetConnectionString("platform_migrator"),
                "charging_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.Policies.UseDurableLocalQueues();
            options.PublishMessage<ReservationExpired>().ToLocalQueue("reservation-expired-tests");
            options.Discovery.IncludeType<ExpireReservationHandler>();
            options.Discovery.IncludeType<ReservationExpiredRecorderHandler>();
        });
        _messaging = builder.Build();
        await _messaging.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_messaging is not null)
        {
            await _messaging.StopAsync();
            _messaging.Dispose();
        }
    }

    [Fact]
    public async Task Expire_ConcurrentChecks_CommitsOneTransitionAuditAndEvent()
    {
        var reservation = await SeedReservationAsync();

        // Deliveries of the same check serialize on the driver lock; later ones find the reservation inactive.
        await _messaging!.TrackActivity().Timeout(TimeSpan.FromSeconds(30)).ExecuteAndWaitAsync(_ =>
            Task.WhenAll(Enumerable.Range(0, 3)
                .Select(_ => _messaging!.MessageBus().InvokeAsync(new ExpireReservation(reservation.Id)))));

        await using var database = fixture.CreateContext();
        var saved = await database.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(ReservationState.Expired, saved.State);
        Assert.Equal(PastDeadline, saved.ExpiredAt);
        var audit = await database.AuditEntries.SingleAsync(entry => entry.ResourceId == reservation.Id);
        Assert.Equal(AuditAction.Updated, audit.Action);
        Assert.Equal("charging-worker", audit.ActorSubject);
        Assert.Equal(new ReservationExpired(reservation.Id, PastDeadline), Assert.Single(Expired.For(reservation.Id)));
    }

    [Fact]
    public async Task Expire_BehindCommittedCancellation_LeavesItCancelled()
    {
        var reservation = await SeedReservationAsync();
        await using var competing = fixture.CreateContext();
        await using var transaction = await competing.Database.BeginTransactionAsync();
        await competing.LockDriverReservationsAsync(reservation.DriverId, CancellationToken.None);
        var cancelled = await competing.Reservations.SingleAsync(value => value.Id == reservation.Id);
        Assert.True(cancelled.Cancel(SlotStart.AddMinutes(5), hasUnresolvedStart: false).IsSuccess);
        await competing.SaveChangesAsync();

        var expiry = _messaging!.MessageBus().InvokeAsync(new ExpireReservation(reservation.Id));
        await fixture.WaitForLockWaiterAsync();
        await transaction.CommitAsync();
        await expiry;

        await using var database = fixture.CreateContext();
        var saved = await database.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(ReservationState.Cancelled, saved.State);
        Assert.Null(saved.ExpiredAt);
        Assert.False(await database.AuditEntries.AnyAsync(entry => entry.ResourceId == reservation.Id));
        Assert.Empty(Expired.For(reservation.Id));
    }

    private ExpiryRecorder Expired => _messaging!.Services.GetRequiredService<ExpiryRecorder>();

    private async Task<Reservation> SeedReservationAsync()
    {
        await using var database = fixture.CreateContext();
        var station = new Station
        {
            Name = "Expiry",
            Address = "Test",
            TimeZoneId = "UTC",
            AccessInstructions = "Test"
        };
        var charger = new Charger { Station = station, Name = "Charger" };
        var connector = new Connector { Charger = charger, ConnectorType = "Type2", PowerSupply = "AC" };
        var driver = Driver.Register(
            "https://test.invalid", Guid.NewGuid().ToString(), EmailAddress.Create("driver@example.test").Value);
        var reservation = Reservation.Book(
            connector,
            driver,
            SlotStart,
            SlotStart.AddHours(1),
            SlotStart,
            new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
        database.Reservations.Add(reservation);
        await database.SaveChangesAsync();
        return reservation;
    }

    public sealed class ExpiryRecorder
    {
        private readonly ConcurrentQueue<ReservationExpired> _events = new();

        public void Add(ReservationExpired message) => _events.Enqueue(message);

        public ReservationExpired[] For(Guid reservationId) =>
            [.. _events.Where(value => value.ReservationId == reservationId)];
    }

    public sealed class ReservationExpiredRecorderHandler(ExpiryRecorder recorder)
    {
        public void Handle(ReservationExpired message) => recorder.Add(message);
    }
}
