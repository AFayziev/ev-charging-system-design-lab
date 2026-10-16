using System.ComponentModel.DataAnnotations;
using EvCharging.WattHarborProviderApi.Persistence;

namespace EvCharging.WattHarborProviderApi.Features.Equipment;

public sealed record ConfigureConnectorRequest([property: Range(0.001, 10000)] decimal PowerKw, bool Enabled);

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
                Enabled = request.Enabled
            },
            cancellationToken);
}
