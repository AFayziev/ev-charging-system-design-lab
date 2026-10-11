using System.Security.Claims;
using EvCharging.Api.Errors;
using EvCharging.Api.Persistence;
using EvCharging.Domain.Equipment;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

internal sealed record StationEditorResponse(
    Guid Id,
    uint Version,
    string Name,
    string Address,
    string AccessInstructions);

internal sealed class GetStationEditorQueryHandler(
    [FromKeyedServices(PlatformPersistenceExtensions.WriteRoute)] PlatformDbContext database,
    IAuthorizationService authorization)
{
    public async Task<Result<StationEditorResponse>> HandleAsync(
        Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .AsNoTracking()
            .SingleOrDefaultAsync(station => station.PublicId == id, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationEditorResponse>(new NotFoundError<Guid>("station", id));
        }

        var permission = await authorization.AuthorizeAsync(user, station, [StationAssignmentRequirement.Instance]);
        return permission.Succeeded
            ? Result.Ok(station.ToEditorResponse())
            : Result.Fail<StationEditorResponse>(new ForbiddenError());
    }
}

internal static class StationEditorResponseMappings
{
    public static StationEditorResponse ToEditorResponse(this Station station) =>
        new(station.PublicId, station.Version, station.Name, station.Address, station.AccessInstructions);
}
