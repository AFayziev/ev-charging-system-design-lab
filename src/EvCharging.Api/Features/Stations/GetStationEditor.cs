using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Domain.Equipment;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

public sealed record GetStationEditorRequest(Guid Id) : IAuthenticatedRequest<StationEditorResponse>;

public sealed record StationEditorResponse(
    Guid Id,
    uint Version,
    string Name,
    string Address,
    string AccessInstructions);

public sealed class GetStationEditorQueryHandler(
    PlatformDbContext database,
    IAuthorizationService authorization)
{
    public async Task<Result<StationEditorResponse>> HandleAsync(
        GetStationEditorRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .AsNoTracking()
            .SingleOrDefaultAsync(station => station.PublicId == request.Id, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationEditorResponse>(new NotFoundError<Guid>("station", request.Id));
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
