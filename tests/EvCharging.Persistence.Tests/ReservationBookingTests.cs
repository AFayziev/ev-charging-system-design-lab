using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EvCharging.Persistence.Tests;

public sealed class ReservationBookingTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
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
        var startsAt = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO charging.reservations (id, connector_id, driver_id, starts_at, ends_at, state, created_at)
            VALUES (uuidv7(), {connector.Id}, {driver.Id}, {startsAt}, {startsAt.AddHours(1)}, {(int)state}, {startsAt})
            """);
        database.Reservations.Add(Booking(connector, driver, startsAt.AddMinutes(30), startsAt.AddHours(2)));
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
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: "ex_reservations_connector_slot" })
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
            INSERT INTO charging.reservations (id, connector_id, driver_id, starts_at, ends_at, state, created_at)
            VALUES (uuidv7(), @connector, @driver, CAST(@start AS timestamptz), CAST(@end AS timestamptz), 0, now())
            """,
            connection);
        command.Parameters.AddWithValue("connector", connector.Id);
        command.Parameters.AddWithValue("driver", driver.Id);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_reservations_slot", exception.ConstraintName);
    }

    private static PlatformDbContext Context(NpgsqlConnection connection) => new(
        new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection).Options);

    private static Reservation Booking(
        Connector connector,
        Driver driver,
        DateTimeOffset start,
        DateTimeOffset end) => Reservation.Book(connector, driver, start, end, start).Value;

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
