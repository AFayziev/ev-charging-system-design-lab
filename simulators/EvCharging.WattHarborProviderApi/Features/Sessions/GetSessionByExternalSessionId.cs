using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Sessions;

internal sealed class GetSessionByExternalSessionIdQueryHandler(ProviderDbContext database)
{
    public async Task<SessionResponse?> HandleAsync(Guid externalSessionId, CancellationToken cancellationToken)
    {
        var session = await database.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.ExternalSessionId == externalSessionId, cancellationToken);
        return session?.ToResponse();
    }
}
