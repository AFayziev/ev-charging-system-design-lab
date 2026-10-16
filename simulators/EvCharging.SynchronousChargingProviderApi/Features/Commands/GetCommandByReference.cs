using EvCharging.SynchronousChargingProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.SynchronousChargingProviderApi.Features.Commands;

internal sealed class GetCommandByReferenceQueryHandler(ProviderDbContext database)
{
    public async Task<CommandResponse?> HandleAsync(Guid reference, CancellationToken cancellationToken)
    {
        var command = await database.Commands
            .AsNoTracking()
            .Include(command => command.Session)
            .SingleOrDefaultAsync(command => command.Reference == reference, cancellationToken);
        return command?.ToResponse();
    }
}
