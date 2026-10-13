using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Contracts;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

public sealed record GetStationRequest(Guid Id) : IUserAwareRequest;

public sealed record StationDetailResponse(
    Guid Id,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string TimeZoneId,
    string AccessInstructions,
    IReadOnlyList<ChargerResponse> Chargers,
    bool CanEdit);

public sealed record ChargerResponse(Guid Id, string Name, IReadOnlyList<ConnectorResponse> Connectors);

public sealed record ConnectorResponse(
    Guid Id,
    int Number,
    string ConnectorType,
    string PowerSupply,
    decimal RatedPowerKW);

public sealed class GetStationQueryHandler(PlatformReadDbContext database, IAuthorizationService authorization)
{
    public async Task<Result<StationDetailResponse>> HandleAsync(
        GetStationRequest request,
        RequestUser user,
        CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .AsNoTracking()
            .Include(station => station.Chargers)
            .ThenInclude(charger => charger.Connectors)
            .SingleOrDefaultAsync(station => station.PublicId == request.Id, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationDetailResponse>(new NotFoundError<Guid>("station", request.Id));
        }

        bool canEdit = user.Identity is not null
            && user.Principal.IsInRole(PlatformRole.Operator.ToClaimValue())
            && (await authorization.AuthorizeAsync(user.Principal, station, [StationAssignmentRequirement.Instance]))
                .Succeeded;
        return Result.Ok(station.ToResponse(canEdit));
    }
}
