using System.Collections.Concurrent;
using System.Linq.Expressions;
using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Features.Reservations;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Domain.Reservations;
using EvCharging.Testing;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class ReservationAdmissionTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>,
    IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlotStart = Now.AddHours(2);
    private readonly SimulatedDeadlock _deadlock = new();
    private readonly CreatedRecorder _created = new();
    private ApiHandlerHost? _api;

    public async Task InitializeAsync() =>
        _api = await ApiHandlerHost.StartAsync<CreateReservationCommandHandler>(
            fixture,
            new FakeTimeProvider(Now),
            services =>
            {
                services.AddSingleton<IInterceptor>(_deadlock);
                services.AddSingleton(_created);
                services.Configure<ReservationPolicyOptions>(options =>
                {
                    options.MaximumOutstandingReservationsPerDriver = 2;
                    options.MaximumBookingDuration = TimeSpan.FromHours(3);
                    options.MaximumAdvanceBookingHorizon = TimeSpan.FromDays(1);
                    options.ArrivalAllowance = TimeSpan.FromMinutes(10);
                });
            },
            messaging =>
            {
                messaging.Policies.UseDurableLocalQueues();
                messaging.PublishMessage<ReservationCreatedEvent>().ToLocalQueue("reservation-created-tests");
                messaging.Discovery.IncludeType<ReservationCreatedRecorderHandler>();
            });

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }

    [Fact]
    public async Task Create_CompetingDriversForOneConnector_OneWinsAndAdjacentSucceeds()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0].PublicId;
        var (_, first) = await fixture.SeedDriverAsync();
        var (_, second) = await fixture.SeedDriverAsync();

        // The driver lock does not separate different drivers, so the connector's exclusion constraint decides.
        // Concurrent inserts can deadlock there; the loser's retry then reports the overlap.
        var results = await Task.WhenAll(
            CreateAsync(first, connector, SlotStart),
            CreateAsync(second, connector, SlotStart));

        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => result.IsFailed);
        var conflict = Assert.IsType<ReservationAdmissionError>(Assert.Single(loser.Errors));
        Assert.Equal("This connector is already reserved during the selected slot.", conflict.Message);
        var loserIdentity = results[0].IsFailed ? first : second;
        Assert.True((await CreateAsync(loserIdentity, connector, SlotStart.AddHours(1))).IsSuccess);
        Assert.Equal(2, await CountAsync(reservation => reservation.Connector!.PublicId == connector));
    }

    [Fact]
    public async Task Create_OneDriverOnTwoConnectors_LockAdmitsOne()
    {
        var connectors = await fixture.SeedConnectorsAsync(2);
        var (driver, identity) = await fixture.SeedDriverAsync();

        // The driver lock serializes the attempts, so the second sees the first's committed reservation.
        var results = await Task.WhenAll(
            CreateAsync(identity, connectors[0].PublicId, SlotStart),
            CreateAsync(identity, connectors[1].PublicId, SlotStart));

        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => result.IsFailed);
        var conflict = Assert.IsType<ReservationAdmissionError>(Assert.Single(loser.Errors));
        Assert.Equal("You already have a reservation during the selected slot.", conflict.Message);
        Assert.Equal(1, await CountAsync(reservation => reservation.DriverId == driver.Id));
    }

    [Fact]
    public async Task Create_DeadlockVictim_RetriesOnFreshStateAndBooksOnce()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        var (driver, identity) = await fixture.SeedDriverAsync();

        // The next save fails as a deadlock victim would. A retry that reused the failed attempt's context or
        // outbox enrollment would book twice and so be rejected by the driver's own overlap constraint.
        _deadlock.FailNextSave();
        Result<ReservationResponse>? result = null;
        await _api!.Messaging.TrackActivity().Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(async Task (IMessageContext _) =>
                result = await CreateAsync(identity, connector.PublicId, SlotStart));

        Assert.True(result!.IsSuccess);
        Assert.True(_deadlock.Fired);
        await using var database = fixture.CreateContext();
        var reservation = await database.Reservations.SingleAsync(value => value.DriverId == driver.Id);
        Assert.Equal(result.Value.Id, reservation.PublicId);
        Assert.Single(_created.For(reservation.Id));
    }

    private Task<Result<ReservationResponse>> CreateAsync(
        UserIdentity identity, Guid connectorId, DateTimeOffset startsAt) =>
        _api!.InvokeAsync<Result<ReservationResponse>>(
            identity, new CreateReservationRequest(connectorId, startsAt, startsAt.AddHours(1)));

    private async Task<int> CountAsync(Expression<Func<Reservation, bool>> predicate)
    {
        await using var database = fixture.CreateContext();
        return await database.Reservations.CountAsync(predicate);
    }

    private sealed class SimulatedDeadlock : SaveChangesInterceptor
    {
        private int _armed;
        private int _fired;

        public bool Fired => Volatile.Read(ref _fired) == 1;

        public void FailNextSave() => Volatile.Write(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Volatile.Write(ref _fired, 1);
                throw new DbUpdateException(
                    "Simulated deadlock victim.",
                    new PostgresException("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected));
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public sealed class CreatedRecorder
    {
        private readonly ConcurrentQueue<ReservationCreatedEvent> _events = new();

        public void Add(ReservationCreatedEvent message) => _events.Enqueue(message);

        public ReservationCreatedEvent[] For(Guid reservationId) =>
            [.. _events.Where(value => value.ReservationId == reservationId)];
    }

    public sealed class ReservationCreatedRecorderHandler(CreatedRecorder recorder)
    {
        public void Handle(ReservationCreatedEvent message) => recorder.Add(message);
    }
}
