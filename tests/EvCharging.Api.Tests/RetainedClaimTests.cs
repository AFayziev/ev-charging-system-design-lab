using EvCharging.Api.Authentication;
using EvCharging.Api.Features.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Extensions;
using EvCharging.Testing;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class RetainedClaimTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    private static readonly DateTimeOffset FirstSlot = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondSlot = FirstSlot.AddMinutes(30);
    private static readonly DateTimeOffset Now = SecondSlot.AddMinutes(1);

    [Fact]
    public async Task Start_BehindAnUncertainStartPastItsDeadline_IsBusyUntilTheStartSettles()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        var (firstDriver, _) = await fixture.SeedDriverAsync();
        var (secondDriver, secondIdentity) = await fixture.SeedDriverAsync();
        var first = await fixture.SeedReservationAsync(connector, firstDriver, FirstSlot, SecondSlot, FirstSlot);
        var second = await fixture.SeedReservationAsync(
            connector, secondDriver, SecondSlot, SecondSlot.AddMinutes(30), FirstSlot);
        var operationId = await SeedUncertainStartAsync(first);
        Assert.Equal(ReservationExpiryOutcome.UnresolvedStart, await ExpireAsync(first, firstDriver));
        await using var api = await ApiHandlerHost.StartAsync<StartChargingCommandHandler>(
            fixture, new FakeTimeProvider(Now));

        var blocked = await StartAsync(api, secondIdentity, second);

        var rejection = Assert.IsType<ChargingStartError>(Assert.Single(blocked.Errors));
        Assert.Equal("connector_busy", rejection.Code);

        await RejectStartAsync(operationId);
        var unblocked = await StartAsync(api, secondIdentity, second);

        Assert.True(unblocked.IsSuccess);
    }

    private Task<Result<StartOperationResponse>> StartAsync(
        ApiHandlerHost api, UserIdentity identity, Reservation reservation) =>
        api.InvokeAsync<Result<StartOperationResponse>>(
            identity, new StartChargingCommand(reservation.PublicId, Guid.NewGuid()));

    // A start the worker authorized and then lost the answer to: one attempt spent, outcome unknown.
    private async Task<Guid> SeedUncertainStartAsync(Reservation reservation)
    {
        await using var database = fixture.CreateContext();
        database.Attach(reservation);
        var operation = Operation.AcceptStart(
            ChargingSession.AcceptStart(reservation, reservation.StartsAt.AddMinutes(1)).Value, Guid.NewGuid());
        database.Operations.Add(operation);
        await database.SaveChangesAsync();
        await database.Operations.Where(value => value.Id == operation.Id).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(value => value.AuthorizedAttemptCount, 1)
                .SetProperty(value => value.RequestLimit, 3)
                .SetProperty(value => value.Outcome, CommandOutcome.Uncertain));
        return operation.Id;
    }

    private async Task<ReservationExpiryOutcome> ExpireAsync(Reservation reservation, Driver driver)
    {
        await using var database = fixture.CreateContext("charging_messaging");
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.LockDriverReservationsAsync(driver.Id, CancellationToken.None);
        var expiring = await database.Reservations.SingleAsync(value => value.Id == reservation.Id);
        var outcome = await database.ExpireReservationAsync(expiring, Now, CancellationToken.None);
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return outcome;
    }

    private async Task RejectStartAsync(Guid operationId)
    {
        await using var database = fixture.CreateContext();
        var operation = await database.Operations.Include(value => value.Session)
            .SingleAsync(value => value.Id == operationId);
        Assert.True(operation.RejectStart().IsSuccess);
        await database.SaveChangesAsync();
    }
}
