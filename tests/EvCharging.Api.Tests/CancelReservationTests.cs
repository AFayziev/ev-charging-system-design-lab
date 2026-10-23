using EvCharging.Api.Features.Reservations;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Extensions;
using EvCharging.Testing;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class CancelReservationTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>,
    IAsyncLifetime
{
    private static readonly DateTimeOffset SlotStart = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PastDeadline = SlotStart.AddMinutes(11);
    private ApiHandlerHost? _api;

    public async Task InitializeAsync() =>
        _api = await ApiHandlerHost.StartAsync<CancelReservationCommandHandler>(
            fixture, new FakeTimeProvider(PastDeadline));

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cancel_BehindCommittedExpiry_RejectsWithoutChanges()
    {
        var connector = (await fixture.SeedConnectorsAsync(1))[0];
        var (driver, identity) = await fixture.SeedDriverAsync();
        Reservation reservation;
        await using (var setup = fixture.CreateContext())
        {
            setup.Attach(connector);
            setup.Attach(driver);
            reservation = Reservation.Book(
                connector,
                driver,
                SlotStart,
                SlotStart.AddHours(1),
                SlotStart,
                new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
            setup.Reservations.Add(reservation);
            await setup.SaveChangesAsync();
        }

        // The expiry worker holds the driver lock and has staged the expiry when the driver's cancellation arrives.
        await using var expiry = fixture.CreateContext("charging_messaging");
        await using var transaction = await expiry.Database.BeginTransactionAsync();
        await expiry.LockDriverReservationsAsync(driver.Id, CancellationToken.None);
        var expiring = await expiry.Reservations.SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(
            ReservationExpiryOutcome.Expired,
            await expiry.ExpireReservationAsync(expiring, PastDeadline, CancellationToken.None));
        await expiry.SaveChangesAsync();

        var cancellation = _api!.InvokeAsync<Result<ReservationResponse>>(
            identity, new CancelReservationCommand(reservation.PublicId));
        await fixture.WaitForLockWaiterAsync();
        await transaction.CommitAsync();
        var result = await cancellation;

        var rejection = Assert.IsType<ReservationCancellationError>(Assert.Single(result.Errors));
        Assert.Equal("Only active reservations can be cancelled.", rejection.Message);
        await using var database = fixture.CreateContext();
        var saved = await database.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(ReservationState.Expired, saved.State);
        Assert.Null(saved.CancelledAt);
        Assert.False(await database.AuditEntries.AnyAsync(entry => entry.ResourceId == reservation.Id));
    }
}
