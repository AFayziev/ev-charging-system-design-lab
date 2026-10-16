using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Commands;

internal sealed class GetCommandByExternalOperationIdQueryHandler(ProviderDbContext database)
{
    public async Task<CommandResponse?> HandleAsync(Guid externalOperationId, CancellationToken cancellationToken)
    {
        var command = await database.Commands
            .AsNoTracking()
            .Include(command => command.Session)
            .SingleOrDefaultAsync(command => command.ExternalOperationId == externalOperationId, cancellationToken);
        return command?.ToResponse();
    }
}
