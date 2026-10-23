using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Extensions;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EvCharging.Persistence.Tests;

public sealed class ReservationBookingTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    [Fact]
    public async Task Start_SecondClaimOnConnector_DatabaseRejects()
    {
        await using var connection = fixture.CreateConnection("platform_writer");
        await using var database = Context(connection);
        var (connector, firstDriver) = await EquipmentAsync(database);
        var secondDriver = Driver.Register(
            "https://test.invalid", Guid.NewGuid().ToString(), EmailAddress.Create("other@example.test").Value);
        var start = DateTimeOffset.UtcNow.AddHours(-3);
        var first = Booking(connector, firstDriver, start, start.AddHours(1));
        var second = Booking(connector, secondDriver, start.AddHours(1), start.AddHours(2));
        database.Reservations.AddRange(first, second);
        database.Sessions.Add(ChargingSession.AcceptStart(first, first.StartsAt).Value);
        await database.SaveChangesAsync();

        database.Sessions.Add(ChargingSession.AcceptStart(second, second.StartsAt).Value);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.True(exception.IsChargingClaimConflict());
    }

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
        var competingDriver = Driver.Register(
            driver.IdentityIssuer, Guid.NewGuid().ToString(), EmailAddress.Create("competitor@example.test").Value);
        database.Drivers.Add(competingDriver);
        await database.SaveChangesAsync();
        var startsAt = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO charging.reservations
                (id, connector_id, driver_id, starts_at, ends_at, arrival_deadline, state, created_at)
            VALUES (uuidv7(), {connector.Id}, {driver.Id}, {startsAt}, {startsAt.AddHours(1)},
                {startsAt.AddMinutes(10)}, {state}, {startsAt})
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
    public async Task Book_DistinctConnectorsOnSameCharger_AllowsConcurrentSlots()
    {
        await using var connection = fixture.CreateConnection("platform_writer");
        await using var database = Context(connection);
        var (first, driver) = await EquipmentAsync(database);
        var second = new Connector
        {
            Charger = first.Charger,
            Number = 2,
            ConnectorType = "CCS2",
            PowerSupply = "DC",
            RatedPowerKW = 50
        };
        var secondDriver = Driver.Register(
            driver.IdentityIssuer, Guid.NewGuid().ToString(), EmailAddress.Create("second@example.test").Value);
        database.Connectors.Add(second);
        database.Drivers.Add(secondDriver);
        await database.SaveChangesAsync();
        var start = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
        database.Reservations.AddRange(
            Booking(first, driver, start, start.AddHours(1)),
            Booking(second, secondDriver, start, start.AddHours(1)));

        await database.SaveChangesAsync();

        var count = await database.Reservations.CountAsync(
            value => value.ConnectorId == first.Id || value.ConnectorId == second.Id);
        Assert.Equal(2, count);
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
                CAST(@start AS timestamptz) + interval '10 minutes', 'active', now())
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
        var connector = new Connector
        {
            Charger = charger,
            Number = 1,
            ConnectorType = "Type2",
            PowerSupply = "AC",
            RatedPowerKW = 22
        };
        var driver = Driver.Register(
            "https://test.invalid", Guid.NewGuid().ToString(), EmailAddress.Create("driver@example.test").Value);
        database.Connectors.Add(connector);
        database.Drivers.Add(driver);
        await database.SaveChangesAsync();
        return (connector, driver);
    }
}
