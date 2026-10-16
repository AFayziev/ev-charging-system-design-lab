using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.WattHarborProviderApi.Features.Commands;

internal sealed class GetCommandQueryHandler(ProviderDbContext database)
{
    public async Task<CommandResponse?> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = await database.Commands.AsNoTracking().Include(command => command.Session)
            .SingleOrDefaultAsync(command => command.PublicId == id, cancellationToken);
        return command?.ToResponse();
    }
}
