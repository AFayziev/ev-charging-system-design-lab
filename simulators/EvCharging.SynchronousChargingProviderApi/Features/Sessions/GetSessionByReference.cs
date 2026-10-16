using EvCharging.SynchronousChargingProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SynchronousChargingProviderApi.Features.Sessions;

internal sealed class GetSessionByReferenceQueryHandler(ProviderDbContext database)
{
    public async Task<SessionResponse?> HandleAsync(Guid reference, CancellationToken cancellationToken)
    {
        var session = await database.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.Reference == reference, cancellationToken);
        return session?.ToResponse();
    }
}
