using EvCharging.Domain.Equipment;
using EvCharging.Testing;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SearchIndexWorker.Tests;

internal static class StationSeed
{
    public static async Task<Station> SeedStationAsync(this PlatformDatabaseFixture fixture)
    {
        await using var database = fixture.CreateContext();
        var station = new Station
        {
            Name = "Search test",
            Address = "1 Synthetic Avenue",
            Latitude = 51.5m,
            Longitude = -0.12m,
            TimeZoneId = "Europe/London",
            AccessInstructions = "Test"
        };
        var charger = new Charger { Station = station, Name = "Charger" };
        database.Connectors.AddRange(
            new Connector
            {
                Charger = charger,
                Number = 1,
                ConnectorType = "Type2",
                PowerSupply = "AC",
                RatedPowerKW = 22
            },
            new Connector
            {
                Charger = charger,
                Number = 2,
                ConnectorType = "CCS2",
                PowerSupply = "DC",
                RatedPowerKW = 50
            });
        await database.SaveChangesAsync();
        return station;
    }

    public static async Task RenameStationAsync(this PlatformDatabaseFixture fixture, Guid stationId, string name)
    {
        await using var database = fixture.CreateContext();
        await database.Stations.Where(station => station.Id == stationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(station => station.Name, name));
    }
}
