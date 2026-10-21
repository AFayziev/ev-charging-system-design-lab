using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Telemetry;

internal sealed class GetSessionSnapshotQueryHandler(ProviderDbContext database)
{
    public async Task<ObservationResponse?> HandleAsync(Guid externalSessionId, CancellationToken cancellationToken)
    {
        var latest = await database.Observations
            .AsNoTracking()
            .Include(observation => observation.Session)
            .Where(observation => observation.Session!.ExternalSessionId == externalSessionId)
            .OrderByDescending(observation => observation.OccurredAt)
            .ThenByDescending(observation => observation.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return latest?.ToResponse();
    }
}
