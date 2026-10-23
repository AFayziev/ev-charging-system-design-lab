using EvCharging.Api.Features.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Testing;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class StartChargingTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    private static readonly DateTimeOffset Boundary = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_CompetingDriversAtSlotBoundary_OneClaimsConnector()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        var (earlierDriver, earlierIdentity) = await fixture.SeedDriverAsync();
        var (laterDriver, laterIdentity) = await fixture.SeedDriverAsync();
        var earlier = await SeedReservationAsync(connector, earlierDriver, Boundary.AddMinutes(-5), Boundary);
        var later = await SeedReservationAsync(connector, laterDriver, Boundary, Boundary.AddMinutes(5));

        // On one clock the two start windows never meet. Two instances whose clocks straddle the boundary each
        // admit their own driver, and neither driver's lock covers the other, so the connector claim decides.
        await using var behind = await ApiHandlerHost.StartAsync<StartChargingCommandHandler>(
            fixture, new FakeTimeProvider(Boundary.AddSeconds(-1)));
        await using var ahead = await ApiHandlerHost.StartAsync<StartChargingCommandHandler>(
            fixture, new FakeTimeProvider(Boundary));
        var results = await Task.WhenAll(
            behind.InvokeAsync<Result<StartOperationResponse>>(
                earlierIdentity, new StartChargingCommand(earlier.PublicId, Guid.NewGuid())),
            ahead.InvokeAsync<Result<StartOperationResponse>>(
                laterIdentity, new StartChargingCommand(later.PublicId, Guid.NewGuid())));

        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => result.IsFailed);
        var conflict = Assert.IsType<ChargingStartError>(Assert.Single(loser.Errors));
        Assert.Equal("connector_busy", conflict.Code);
        await using var database = fixture.CreateContext();
        Assert.Equal(1, await database.Sessions.CountAsync(session => session.ConnectorId == connector.Id));
    }

    private async Task<Reservation> SeedReservationAsync(
        Connector connector, Driver driver, DateTimeOffset start, DateTimeOffset end)
    {
        await using var database = fixture.CreateContext();
        database.Attach(connector);
        database.Attach(driver);
        var reservation = Reservation.Book(
            connector,
            driver,
            start,
            end,
            start,
            new ReservationBookingLimits(
                2, TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
        database.Reservations.Add(reservation);
        await database.SaveChangesAsync();
        return reservation;
    }
}
