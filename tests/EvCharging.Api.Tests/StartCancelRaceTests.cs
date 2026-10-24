using EvCharging.Api.Authentication;
using EvCharging.Api.Features.Charging;
using EvCharging.Api.Features.Reservations;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using EvCharging.Testing;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Api.Tests;

// The start and the cancellation of one reservation serialize on the driver's lock. Each test lets one request
// commit first and holds the other behind it, so the order is forced instead of left to timing.
public sealed class StartCancelRaceTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    private static readonly DateTimeOffset SlotStart = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = SlotStart.AddMinutes(1);

    [Fact]
    public async Task Start_BehindCommittedCancellation_RejectsWithoutAnIntent()
    {
        var (reservationId, driverId, identity) = await SeedReservationAsync();
        await using var api = await ApiHandlerHost.StartAsync<StartChargingCommandHandler>(
            fixture, new FakeTimeProvider(Now));
        await using var holder = fixture.CreateContext();
        await using var transaction = await HoldDriverLockAsync(holder, driverId);
        var cancelling = await holder.Reservations.SingleAsync(value => value.Id == reservationId);
        Assert.True(cancelling.Cancel(Now, hasUnresolvedStart: false).IsSuccess);
        await holder.SaveChangesAsync();

        var start = api.InvokeAsync<Result<StartOperationResponse>>(
            identity, new StartChargingCommand(cancelling.PublicId, Guid.NewGuid()));
        await fixture.WaitForLockWaiterAsync();
        await transaction.CommitAsync();
        var result = await start;

        var rejection = Assert.IsType<ChargingStartError>(Assert.Single(result.Errors));
        Assert.Equal(ChargingStartError.ReservationInactiveCode, rejection.Code);
        await using var database = fixture.CreateContext();
        var saved = await database.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservationId);
        Assert.Equal(ReservationState.Cancelled, saved.State);
        Assert.False(await database.Sessions.AnyAsync(value => value.ReservationId == reservationId));
        Assert.False(await database.Operations.AnyAsync(value => value.Session!.ReservationId == reservationId));
    }

    [Fact]
    public async Task Cancel_BehindCommittedStart_RejectsAndKeepsTheStart()
    {
        var (reservationId, driverId, identity) = await SeedReservationAsync();
        await using var api = await ApiHandlerHost.StartAsync<CancelReservationCommandHandler>(
            fixture, new FakeTimeProvider(Now));
        await using var holder = fixture.CreateContext();
        await using var transaction = await HoldDriverLockAsync(holder, driverId);
        var starting = await holder.Reservations.SingleAsync(value => value.Id == reservationId);
        holder.Operations.Add(Operation.AcceptStart(ChargingSession.AcceptStart(starting, Now).Value, Guid.NewGuid()));
        await holder.SaveChangesAsync();

        var cancellation = api.InvokeAsync<Result<ReservationResponse>>(
            identity, new CancelReservationCommand(starting.PublicId));
        await fixture.WaitForLockWaiterAsync();
        await transaction.CommitAsync();
        var result = await cancellation;

        Assert.IsType<ReservationCancellationError>(Assert.Single(result.Errors));
        await using var database = fixture.CreateContext();
        var saved = await database.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservationId);
        Assert.Equal(ReservationState.Active, saved.State);
        Assert.Null(saved.CancelledAt);
        var session = await database.Sessions.AsNoTracking().SingleAsync(value => value.ReservationId == reservationId);
        Assert.Equal(SessionLifecycle.Starting, session.Lifecycle);
    }

    // The request that commits first holds the driver's lock until the test commits it.
    private static async Task<IDbContextTransaction> HoldDriverLockAsync(
        PlatformDbContext holder, Guid driverId)
    {
        var transaction = await holder.Database.BeginTransactionAsync();
        await holder.LockDriverReservationsAsync(driverId, CancellationToken.None);
        return transaction;
    }

    private async Task<(Guid ReservationId, Guid DriverId, UserIdentity Identity)> SeedReservationAsync()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        var (driver, identity) = await fixture.SeedDriverAsync();
        var reservation = await fixture.SeedReservationAsync(
            connector, driver, SlotStart, SlotStart.AddHours(1), SlotStart);
        return (reservation.Id, driver.Id, identity);
    }
}
