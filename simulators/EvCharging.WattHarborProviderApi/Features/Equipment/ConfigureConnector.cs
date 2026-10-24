using System.ComponentModel.DataAnnotations;
using EvCharging.WattHarborProviderApi.Persistence;

namespace EvCharging.WattHarborProviderApi.Features.Equipment;

/// <param name="EnergyLimitKWh">The energy a vehicle takes before the provider ends its session; null for none.</param>
public sealed record ConfigureConnectorRequest(
    [property: Range(0.001, 10000)] decimal PowerKw,
    bool Enabled,
    [property: Range(0.001, 10000)] decimal? EnergyLimitKWh);

internal sealed class ConfigureConnectorCommandHandler(ProviderDbContext database)
{
    public Task HandleAsync(
        string stationId,
        string chargerId,
        string connectorId,
        ConfigureConnectorRequest request,
        CancellationToken cancellationToken) =>
        database.ConfigureConnectorAsync(
            new ProviderConnector
            {
                StationId = stationId,
                ChargerId = chargerId,
                ConnectorId = connectorId,
                PowerKw = request.PowerKw,
                Enabled = request.Enabled,
                EnergyLimitKWh = request.EnergyLimitKWh
            },
            cancellationToken);
}
