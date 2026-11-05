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
    private readonly CreatedRecorder _created = new();
    private ApiHandlerHost? _api;

    public async Task InitializeAsync() =>
        _api = await ApiHandlerHost.StartAsync<CreateReservationCommandHandler>(
            fixture,
            new FakeTimeProvider(Now),
            services =>
            {
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

    private Task<Result<ReservationResponse>> CreateAsync(
        UserIdentity identity, Guid connectorId, DateTimeOffset startsAt) =>
        _api!.InvokeAsync<Result<ReservationResponse>>(
            identity, new CreateReservationRequest(connectorId, startsAt, startsAt.AddHours(1)));

    private async Task<int> CountAsync(Expression<Func<Reservation, bool>> predicate)
    {
        await using var database = fixture.CreateContext();
        return await database.Reservations.CountAsync(predicate);
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
