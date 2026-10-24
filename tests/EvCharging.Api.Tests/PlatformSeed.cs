using EvCharging.Api.Authentication;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Testing;

namespace EvCharging.Api.Tests;

internal static class PlatformSeed
{
    private const string Issuer = "https://test.invalid";

    public static async Task<Connector[]> SeedConnectorsAsync(this PlatformDatabaseFixture fixture, int count)
    {
        await using var database = fixture.CreateContext();
        var station = new Station
        {
            Name = "Handler test",
            Address = "Synthetic",
            TimeZoneId = "Europe/London",
            AccessInstructions = "Test"
        };
        var charger = new Charger { Station = station, Name = "Charger" };
        var connectors = Enumerable.Range(1, count)
            .Select(number => new Connector
            {
                Charger = charger,
                Number = number,
                ConnectorType = "Type2",
                PowerSupply = "AC",
                RatedPowerKW = 22
            })
            .ToArray();
        database.Connectors.AddRange(connectors);
        await database.SaveChangesAsync();
        return connectors;
    }

    public static async Task<(Driver Driver, UserIdentity Identity)> SeedDriverAsync(
        this PlatformDatabaseFixture fixture)
    {
        await using var database = fixture.CreateContext();
        var identity = new UserIdentity(Issuer, Guid.NewGuid().ToString());
        var driver = Driver.Register(
            identity.Issuer, identity.Subject, EmailAddress.Create("driver@example.test").Value);
        database.Drivers.Add(driver);
        await database.SaveChangesAsync();
        return (driver, identity);
    }

    public static async Task<Reservation> SeedReservationAsync(
        this PlatformDatabaseFixture fixture,
        Connector connector,
        Driver driver,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset bookedAt)
    {
        await using var database = fixture.CreateContext();
        database.Attach(connector);
        database.Attach(driver);
        var reservation = Reservation.Book(
            connector,
            driver,
            start,
            end,
            bookedAt,
            new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
        database.Reservations.Add(reservation);
        await database.SaveChangesAsync();
        return reservation;
    }
}
