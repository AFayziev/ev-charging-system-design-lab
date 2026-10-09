using EvCharging.Api.Errors;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

internal sealed record GetStationRequest(Guid Id);

internal sealed record StationDetailResponse(
    Guid Id,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string TimeZoneId,
    string AccessInstructions,
    IReadOnlyList<ChargerResponse> Chargers);

internal sealed record ChargerResponse(Guid Id, string Name, IReadOnlyList<EvseResponse> Evses);

internal sealed record EvseResponse(Guid Id, int Number, IReadOnlyList<ConnectorResponse> Connectors);

internal sealed record ConnectorResponse(
    Guid Id,
    int Number,
    string ConnectorType,
    string PowerSupply,
    decimal RatedPowerKW);

internal sealed class GetStationQueryHandler(PlatformDbContext database)
{
    public async Task<Result<StationDetailResponse>> HandleAsync(
        GetStationRequest request,
        CancellationToken cancellationToken)
    {
        var station = await database.Stations
            .AsNoTracking()
            .Include(station => station.Chargers)
            .ThenInclude(charger => charger.Evses)
            .ThenInclude(evse => evse.Connectors)
            .SingleOrDefaultAsync(station => station.PublicId == request.Id, cancellationToken);
        if (station is null)
        {
            return Result.Fail<StationDetailResponse>(new NotFoundError<Guid>("station", request.Id));
        }

        return Result.Ok(station.ToResponse());
    }
}
