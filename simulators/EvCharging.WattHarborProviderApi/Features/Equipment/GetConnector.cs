using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Equipment;

internal sealed record ConnectorResponse(decimal PowerKw, bool Enabled, bool Occupied);

internal sealed class GetConnectorQueryHandler(ProviderDbContext database)
{
    public async Task<ConnectorResponse?> HandleAsync(
        string stationId, string chargerId, string connectorId, CancellationToken cancellationToken)
    {
        var connector = await database.Connectors
            .AsNoTracking()
            .SingleOrDefaultAsync(
                connector => connector.StationId == stationId && connector.ChargerId == chargerId
                    && connector.ConnectorId == connectorId,
                cancellationToken);
        return connector is null ? null : new(connector.PowerKw, connector.Enabled, connector.ActiveCommandId.HasValue);
    }
}
