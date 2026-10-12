using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EvCharging.Persistence.Tests;

public sealed class ReservationBookingTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    [Fact]
    public async Task LockDriver_TransientContention_RecognizesWrappedFailureForMessageRetry()
    {
        await using var connection = fixture.CreateConnection("platform_writer");
        await using var holder = Context(connection);
        var (_, driver) = await EquipmentAsync(holder);
        await using var heldTransaction = await holder.Database.BeginTransactionAsync();
        await holder.LockDriverReservationsAsync(driver.Id, CancellationToken.None);

        await using var competingConnection = fixture.CreateConnection("platform_writer");
        await using var competing = Context(competingConnection);
        await using var competingTransaction = await competing.Database.BeginTransactionAsync();
        await competing.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            competing.LockDriverReservationsAsync(driver.Id, CancellationToken.None));

        Assert.True(exception.IsTransientDatabaseFailure());
    }

    [Fact]
    public async Task Expire_CompetingChecks_CommitsOneTransitionAndAudit()
    {
        await using var setupConnection = fixture.CreateConnection("platform_writer");
        await using var setup = Context(setupConnection);
        var (connector, driver) = await EquipmentAsync(setup);
        var end = DateTimeOffset.UtcNow.AddMinutes(-1);
        var reservation = Booking(connector, driver, end.AddHours(-1), end);
        setup.Reservations.Add(reservation);
        await setup.SaveChangesAsync();

        await Task.WhenAll(
            ExpireAsync("charging_messaging"),
            ExpireAsync("charging_messaging"),
            ExpireAsync("platform_writer"));

        var saved = await setup.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(ReservationState.Expired, saved.State);
        Assert.NotNull(saved.ExpiredAt);
        var audit = await setup.AuditEntries.SingleAsync(entry => entry.ResourceId == reservation.Id);
        Assert.Equal(AuditAction.Updated, audit.Action);
        Assert.Equal(0, audit.Details.GetProperty("before").GetProperty("state").GetInt32());
        Assert.Equal(3, audit.Details.GetProperty("after").GetProperty("state").GetInt32());
        Assert.Equal(
            saved.ExpiredAt.Value.ToUnixTimeMilliseconds(),
            audit.Details.GetProperty("after").GetProperty("expiredAt").GetDateTimeOffset().ToUnixTimeMilliseconds());

        async Task ExpireAsync(string role)
        {
            await using var connection = fixture.CreateConnection(role);
            var writer = new AuditEntryWriter(new ExpiryActor(), TimeProvider.System);
            await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(connection)
                .AddInterceptors(new AuditSaveChangesInterceptor(writer))
                .Options);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.LockDriverReservationsAsync(driver.Id, CancellationToken.None);
            var loaded = await context.Reservations.SingleAsync(value => value.Id == reservation.Id);
            await context.ExpireReservationsAsync([loaded], DateTimeOffset.UtcNow, CancellationToken.None);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }

    private sealed class ExpiryActor : IAuditActorAccessor
    {
        public AuditActor GetActor() => AuditActor.Service("expiry-test");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancel_ExpiryRace_DriverLockPreservesFirstTransition(bool cancellationFirst)
    {
        await using var setupConnection = fixture.CreateConnection("platform_writer");
        await using var setup = Context(setupConnection);
        var (connector, driver) = await EquipmentAsync(setup);
        var end = DateTimeOffset.UtcNow.AddMinutes(-1);
        var reservation = Booking(connector, driver, end.AddHours(-1), end);
        setup.Reservations.Add(reservation);
        await setup.SaveChangesAsync();

        await using var firstConnection = fixture.CreateConnection(
            cancellationFirst ? "platform_writer" : "charging_messaging");
        await using var first = AuditedContext(firstConnection);
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await first.LockDriverReservationsAsync(driver.Id, CancellationToken.None);
        var loaded = await first.Reservations.SingleAsync(value => value.Id == reservation.Id);
        var transitionedAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (cancellationFirst)
        {
            Assert.True(loaded.Cancel(transitionedAt, false).IsSuccess);
        }
        else
        {
            await first.ExpireReservationsAsync([loaded], transitionedAt, CancellationToken.None);
        }

        var competing = CompeteAsync();
        await first.SaveChangesAsync();
        await firstTransaction.CommitAsync();
        await competing;

        var saved = await setup.Reservations.AsNoTracking().SingleAsync(value => value.Id == reservation.Id);
        Assert.Equal(cancellationFirst ? ReservationState.Cancelled : ReservationState.Expired, saved.State);
        Assert.Equal(cancellationFirst ? transitionedAt : (DateTimeOffset?)null, saved.CancelledAt);
        Assert.Equal(cancellationFirst ? (DateTimeOffset?)null : transitionedAt, saved.ExpiredAt);
        Assert.Single(await setup.AuditEntries.Where(entry => entry.ResourceId == reservation.Id).ToListAsync());

        async Task CompeteAsync()
        {
            await using var connection = fixture.CreateConnection(
                cancellationFirst ? "charging_messaging" : "platform_writer");
            await using var context = AuditedContext(connection);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.LockDriverReservationsAsync(driver.Id, CancellationToken.None);
            var current = await context.Reservations.SingleAsync(value => value.Id == reservation.Id);
            if (cancellationFirst)
            {
                var outcomes = await context.ExpireReservationsAsync(
                    [current], DateTimeOffset.UtcNow, CancellationToken.None);
                Assert.Equal(ReservationExpiryOutcome.AlreadyInactive, outcomes[current.Id]);
            }
            else
            {
                Assert.True(current.Cancel(DateTimeOffset.UtcNow, false).IsFailed);
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }

    private static PlatformDbContext AuditedContext(NpgsqlConnection connection) => new(
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new AuditEntryWriter(new ExpiryActor(), TimeProvider.System)))
            .Options);

    [Theory]
    [InlineData(ReservationState.Active, true)]
    [InlineData(ReservationState.Consumed, true)]
    [InlineData(ReservationState.Cancelled, false)]
    [InlineData(ReservationState.Expired, false)]
    public async Task Book_OverlappingStates_EnforcesApplicableAllocations(ReservationState state, bool conflicts)
    {
        await using var connection = fixture.CreateConnection("platform_writer");
        await using var database = Context(connection);
        var (connector, driver) = await EquipmentAsync(database);
        var competingDriver = new Driver
        {
            IdentityIssuer = driver.IdentityIssuer,
            IdentitySubject = Guid.NewGuid().ToString()
        };
        database.Drivers.Add(competingDriver);
        await database.SaveChangesAsync();
        var startsAt = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO charging.reservations
                (id, connector_id, driver_id, starts_at, ends_at, arrival_deadline, state, created_at)
            VALUES (uuidv7(), {connector.Id}, {driver.Id}, {startsAt}, {startsAt.AddHours(1)},
                {startsAt.AddMinutes(10)}, {(int)state}, {startsAt})
            """);
        database.Reservations.Add(Booking(connector, competingDriver, startsAt.AddMinutes(30), startsAt.AddHours(2)));
        if (conflicts)
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.ExclusionViolation, postgres.SqlState);
            Assert.Equal("ex_reservations_connector_slot", postgres.ConstraintName);
        }
        else
        {
            await database.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Book_CompetingConnections_OneWinsAndAdjacentSucceeds()
    {
        await using var setupConnection = fixture.CreateConnection("platform_writer");
        await using var setup = Context(setupConnection);
        var (connector, driver) = await EquipmentAsync(setup);
        var startsAt = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.Equal(1, outcomes.Count(success => success));
        setup.Reservations.Add(Booking(connector, driver, startsAt.AddHours(1), startsAt.AddHours(2)));
        await setup.SaveChangesAsync();
        Assert.Equal(2, await setup.Reservations.CountAsync(reservation => reservation.ConnectorId == connector.Id));

        async Task<bool> AttemptAsync()
        {
            await using var connection = fixture.CreateConnection("platform_writer");
            await using var database = Context(connection);
            var loadedConnector = await database.Connectors.SingleAsync(value => value.Id == connector.Id);
            var loadedDriver = await database.Drivers.SingleAsync(value => value.Id == driver.Id);
            database.Reservations.Add(Booking(loadedConnector, loadedDriver, startsAt, startsAt.AddHours(1)));
            try
            {
                await database.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException exception) when (
                exception.IsReservationOverlap() || exception.IsDriverReservationOverlap())
            {
                return false;
            }
        }
    }

    [Theory]
    [InlineData("2030-01-01T10:00:00Z", "2030-01-01T10:00:00Z")]
    [InlineData("2030-01-01T10:00:00Z", "2030-01-01T09:00:00Z")]
    [InlineData("-infinity", "2030-01-01T11:00:00Z")]
    [InlineData("2030-01-01T10:00:00Z", "infinity")]
    public async Task Book_InvalidSlot_DatabaseRejects(string start, string end)
    {
        await using var connection = fixture.CreateConnection("platform_writer");
        await using var database = Context(connection);
        var (connector, driver) = await EquipmentAsync(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO charging.reservations
                (id, connector_id, driver_id, starts_at, ends_at, arrival_deadline, state, created_at)
            VALUES (uuidv7(), @connector, @driver, CAST(@start AS timestamptz), CAST(@end AS timestamptz),
                CAST(@start AS timestamptz) + interval '10 minutes', 0, now())
            """,
            connection);
        command.Parameters.AddWithValue("connector", connector.Id);
        command.Parameters.AddWithValue("driver", driver.Id);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private static PlatformDbContext Context(NpgsqlConnection connection) => new(
        new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection).Options);

    private static Reservation Booking(
        Connector connector,
        Driver driver,
        DateTimeOffset start,
        DateTimeOffset end) => Reservation.Book(
            connector,
            driver,
            start,
            end,
            start,
            new ReservationBookingLimits(
                2, TimeSpan.FromHours(3), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;

    private static async Task<(Connector, Driver)> EquipmentAsync(PlatformDbContext database)
    {
        var station = new Station
        {
            Name = "Booking test",
            Address = "Synthetic",
            TimeZoneId = "Europe/London",
            AccessInstructions = "Test"
        };
        var charger = new Charger { Station = station, Name = "Charger" };
        var evse = new Evse { Charger = charger, Number = 1 };
        var connector = new Connector
        {
            Evse = evse,
            Number = 1,
            ConnectorType = "Type2",
            PowerSupply = "AC",
            RatedPowerKW = 22
        };
        var driver = new Driver
        {
            IdentityIssuer = "https://test.invalid",
            IdentitySubject = Guid.NewGuid().ToString()
        };
        database.Connectors.Add(connector);
        database.Drivers.Add(driver);
        await database.SaveChangesAsync();
        return (connector, driver);
    }
}
