using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Charging;
using EvCharging.Api.Features.Stations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Testing;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class StationAvailabilityTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly UserIdentity Anonymous = new("https://test.invalid", "anonymous");

    [Fact]
    public async Task Get_ConnectorsWithoutReservationsOrSessions_AreAvailable()
    {
        var connectors = await fixture.SeedConnectorsAsync(2);

        var station = await GetAsync(connectors[0]);

        Assert.Equal(2, station.Connectors.Count);
        Assert.All(station.Connectors, connector => Assert.True(connector.Available));
    }

    // The slot is half-open: it holds the connector from its start up to, not including, its end.
    [Theory]
    [InlineData(ReservationState.Active, -30, 30, false)]
    [InlineData(ReservationState.Consumed, -30, 30, false)]
    [InlineData(ReservationState.Cancelled, -30, 30, true)]
    [InlineData(ReservationState.Expired, -30, 30, true)]
    [InlineData(ReservationState.Active, -60, -30, true)]
    [InlineData(ReservationState.Active, -30, 0, true)]
    [InlineData(ReservationState.Active, 0, 30, false)]
    [InlineData(ReservationState.Active, 30, 60, true)]
    public async Task Get_Reservation_BlocksOnlyWhileApplicableAndCoveringNow(
        ReservationState state, int startMinutes, int endMinutes, bool available)
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        await SeedReservationAsync(connector, Now.AddMinutes(startMinutes), Now.AddMinutes(endMinutes), state);

        var station = await GetAsync(connector);

        Assert.Equal(available, Assert.Single(station.Connectors).Available);
    }

    [Theory]
    [InlineData(SessionLifecycle.Starting, false)]
    [InlineData(SessionLifecycle.Active, false)]
    [InlineData(SessionLifecycle.Stopping, false)]
    [InlineData(SessionLifecycle.Completed, true)]
    [InlineData(SessionLifecycle.Failed, true)]
    public async Task Get_Session_BlocksWhileNonterminal(SessionLifecycle lifecycle, bool available)
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        await SeedSessionAsync(connector, Now.AddHours(-3), lifecycle);

        var station = await GetAsync(connector);

        // The slot ended two hours ago, so only the session can hold the connector.
        Assert.Equal(available, Assert.Single(station.Connectors).Available);
    }

    [Fact]
    public async Task Get_SeveralStations_CountsEachStationSeparately()
    {
        var busy = await fixture.SeedConnectorsAsync(3);
        var free = await fixture.SeedConnectorsAsync(2);
        await SeedReservationAsync(busy[0], Now.AddMinutes(-30), Now.AddMinutes(30), ReservationState.Active);
        await SeedReservationAsync(busy[2], Now.AddMinutes(-30), Now.AddMinutes(30), ReservationState.Consumed);

        var response = await InvokeAsync(
            new GetStationAvailabilityRequest([StationId(busy[0]), StationId(free[0]), StationId(busy[0])]));

        Assert.Equal(2, response.Stations.Count);
        var busyStation = Assert.Single(response.Stations, station => station.Id == StationId(busy[0]));
        Assert.Equal(
            (1, 3),
            (busyStation.Connectors.Count(connector => connector.Available), busyStation.Connectors.Count));
        var freeStation = Assert.Single(response.Stations, station => station.Id == StationId(free[0]));
        Assert.Equal(
            (2, 2),
            (freeStation.Connectors.Count(connector => connector.Available), freeStation.Connectors.Count));
    }

    [Fact]
    public async Task Get_UnknownStation_IsOmitted()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];

        var response = await InvokeAsync(new GetStationAvailabilityRequest([Guid.NewGuid(), StationId(connector)]));

        Assert.Equal(StationId(connector), Assert.Single(response.Stations).Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Get_IdCountOutOfRange_IsRejected(int count)
    {
        await using var host = await StartAsync<GetStationAvailabilityQueryHandler>(Now);
        var request = new GetStationAvailabilityRequest(
            Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray());

        var result = await host.InvokeAsync<Result<StationAvailabilityListResponse>>(Anonymous, request);

        Assert.True(result.HasError<ValidationError>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public async Task Get_IdCountAtTheLimits_IsAccepted(int count)
    {
        var response = await InvokeAsync(new GetStationAvailabilityRequest(
            Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray()));

        Assert.Empty(response.Stations);
    }

    [Fact]
    public async Task Get_AvailableConnector_DoesNotAuthorizeAStartWithoutAnEligibleReservation()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        var (driver, identity) = await fixture.SeedDriverAsync();
        var reservation = await fixture.SeedReservationAsync(
            connector, driver, Now.AddMinutes(30), Now.AddMinutes(60), Now);
        Assert.True(Assert.Single((await GetAsync(connector)).Connectors).Available);

        await using var host = await StartAsync<StartChargingCommandHandler>(Now);
        var result = await host.InvokeAsync<Result<StartOperationResponse>>(
            identity, new StartChargingCommand(reservation.PublicId, Guid.NewGuid()));

        var error = Assert.IsType<ChargingStartError>(Assert.Single(result.Errors));
        Assert.Equal(ChargingStartError.TooEarlyCode, error.Code);
        await using var database = fixture.CreateContext();
        Assert.False(await database.Sessions.AnyAsync(session => session.ConnectorId == connector.Id));
    }

    private async Task<StationAvailabilityResponse> GetAsync(Connector connector) =>
        Assert.Single((await InvokeAsync(new GetStationAvailabilityRequest([StationId(connector)]))).Stations);

    private async Task<StationAvailabilityListResponse> InvokeAsync(GetStationAvailabilityRequest request)
    {
        await using var host = await StartAsync<GetStationAvailabilityQueryHandler>(Now);
        var result = await host.InvokeAsync<Result<StationAvailabilityListResponse>>(Anonymous, request);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private Task<ApiHandlerHost> StartAsync<THandler>(DateTimeOffset now) =>
        ApiHandlerHost.StartAsync<THandler>(
            fixture,
            new FakeTimeProvider(now),
            services => services.AddDbContext<PlatformReadDbContext>(
                options => options.UseNpgsql(fixture.GetDataSource("platform_reader"))));

    private static Guid StationId(Connector connector) => connector.Charger!.Station!.PublicId;

    private async Task<Reservation> SeedReservationAsync(
        Connector connector, DateTimeOffset start, DateTimeOffset end, ReservationState state)
    {
        var (driver, _) = await fixture.SeedDriverAsync();
        var reservation = await fixture.SeedReservationAsync(connector, driver, start, end, start);
        if (state == ReservationState.Active)
        {
            return reservation;
        }

        await using var database = fixture.CreateContext();
        database.Attach(reservation);
        switch (state)
        {
            case ReservationState.Consumed:
                Assert.True(reservation.Consume().IsSuccess);
                break;
            case ReservationState.Cancelled:
                Assert.True(reservation.Cancel(start, hasUnresolvedStart: false).IsSuccess);
                break;
            default:
                Assert.Equal(
                    ReservationExpiryOutcome.Expired,
                    reservation.ExpireAtArrivalDeadline(reservation.ArrivalDeadline, hasUnresolvedStart: false));
                break;
        }

        await database.SaveChangesAsync();
        return reservation;
    }

    // The slot ended before the clock, so a session that still holds the connector is an overrun.
    private async Task<ChargingSession> SeedSessionAsync(
        Connector connector, DateTimeOffset slotStart, SessionLifecycle lifecycle)
    {
        var (driver, _) = await fixture.SeedDriverAsync();
        var reservation = await fixture.SeedReservationAsync(
            connector, driver, slotStart, slotStart.AddHours(1), slotStart);
        await using var database = fixture.CreateContext();
        database.Attach(reservation);
        var session = ChargingSession.AcceptStart(reservation, slotStart.AddMinutes(1)).Value;
        database.Sessions.Add(session);
        await database.SaveChangesAsync();
        if (lifecycle != SessionLifecycle.Starting)
        {
            await database.Sessions.Where(value => value.Id == session.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.Lifecycle, lifecycle));
        }

        return session;
    }
}
