using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Persistence;

internal static class ProviderSeedData
{
    public static void Seed(DbContext context)
    {
        var connectors = context.Set<ProviderConnector>();
        foreach (var connector in CreateConnectors())
        {
            if (connectors.Find(connector.StationId, connector.ChargerId, connector.ConnectorId) is null)
            {
                connectors.Add(connector);
            }
        }

        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        var connectors = context.Set<ProviderConnector>();
        foreach (var connector in CreateConnectors())
        {
            var existing = await connectors.FindAsync(
                [connector.StationId, connector.ChargerId, connector.ConnectorId], cancellationToken);
            if (existing is null)
            {
                connectors.Add(connector);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<ProviderConnector> CreateConnectors()
    {
        var stations = new[]
        {
            ("north", 1, 2, 22m),
            ("canal", 2, 2, 11m),
            ("library", 1, 1, 7.4m),
            ("garden", 2, 1, 22m)
        };
        foreach (var (station, chargerCount, connectorCount, powerKw) in stations)
        {
            for (int charger = 1; charger <= chargerCount; charger++)
            {
                for (int connector = 1; connector <= connectorCount; connector++)
                {
                    yield return new ProviderConnector
                    {
                        StationId = station,
                        ChargerId = $"charger-{charger}",
                        ConnectorId = $"connector-{connector}",
                        PowerKw = powerKw,
                        Enabled = true
                    };
                }
            }
        }
    }
}
