using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Sessions;

internal sealed class GetSessionQueryHandler(ProviderDbContext database)
{
    public async Task<SessionResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = await database.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(session => session.PublicId == id, cancellationToken);
        return session?.ToResponse();
    }
}
