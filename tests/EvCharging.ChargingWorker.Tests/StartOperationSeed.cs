using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using EvCharging.Testing;

namespace EvCharging.ChargingWorker.Tests;

internal static class StartOperationSeed
{
    // A driver with a booking that starts now, the provider mapping of its connector, and the accepted start.
    public static async Task<Operation> CreateAsync(
        PlatformDatabaseFixture fixture, DateTimeOffset now, string? integrationKey)
    {
        await using var database = fixture.CreateContext();
        var station = new Station { Name = "Test", Address = "Test", TimeZoneId = "UTC", AccessInstructions = "Test" };
        var charger = new Charger { Station = station, Name = "Test" };
        var connector = new Connector { Charger = charger, ConnectorType = "Type2", PowerSupply = "AC" };
        var driver = Driver.Register(
            "https://test.invalid",
            Guid.NewGuid().ToString(),
            EmailAddress.Create("driver@example.test").Value);
        database.Connectors.Add(connector);
        database.Drivers.Add(driver);
        await database.SaveChangesAsync();
        var reservation = Reservation.Book(
            connector,
            driver,
            now,
            now.AddHours(1),
            now,
            new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
        var operation = Operation.AcceptStart(ChargingSession.AcceptStart(reservation, now).Value, Guid.NewGuid());
        database.Operations.Add(operation);
        var provider = new ChargingProvider
        {
            Name = $"Edited display {Guid.NewGuid()}",
            IntegrationKey = integrationKey
        };
        var providerStation = new ProviderStation
        {
            ChargingProvider = provider,
            Station = station,
            ExternalId = Guid.NewGuid().ToString()
        };
        var providerCharger = new ProviderCharger
        {
            ProviderStation = providerStation,
            Charger = charger,
            ExternalId = "charger"
        };
        database.ProviderConnectors.Add(new()
        {
            ProviderCharger = providerCharger,
            Connector = connector,
            ExternalId = "connector"
        });
        await database.SaveChangesAsync();
        return operation;
    }
}
