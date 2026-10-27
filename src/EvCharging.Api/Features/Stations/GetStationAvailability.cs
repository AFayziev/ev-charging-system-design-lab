using EvCharging.Api.Errors;
using EvCharging.Api.Pagination;
using EvCharging.Persistence;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

public sealed record GetStationAvailabilityRequest(Guid[]? Ids = null);

public sealed record StationAvailabilityListResponse(IReadOnlyList<StationAvailabilityResponse> Stations);

public sealed record StationAvailabilityResponse(Guid Id, IReadOnlyList<ConnectorAvailabilityResponse> Connectors);

public sealed record ConnectorAvailabilityResponse(Guid Id, bool Available);

/// <summary>
/// Advisory availability at request time, from sessions and reservations only. Booking and charging start check
/// eligibility themselves and never consult this view.
/// </summary>
public sealed class GetStationAvailabilityQueryHandler(PlatformReadDbContext database, TimeProvider clock)
{
    public async Task<Result<StationAvailabilityListResponse>> HandleAsync(
        GetStationAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var ids = request.Ids?.Distinct().ToArray() ?? [];
        if (ids.Length is < 1 or > CursorPagination.MaximumPageSize)
        {
            return Result.Fail<StationAvailabilityListResponse>(new ValidationError(
                nameof(request.Ids), $"Between 1 and {CursorPagination.MaximumPageSize} station ids are required."));
        }

        var now = clock.GetUtcNow();
        var connectors = await database.Connectors
            .AsNoTracking()
            .Where(connector => ids.Contains(connector.Charger!.Station!.PublicId))
            .OrderBy(connector => connector.Id)
            .Select(
                connector => new
                {
                    StationId = connector.Charger!.Station!.PublicId,
                    connector.PublicId,
                    Available = !database.Sessions.Nonterminal()
                        .Any(session => session.ConnectorId == connector.Id)
                        && !database.Reservations.ApplicableAt(now)
                            .Any(reservation => reservation.ConnectorId == connector.Id)
                })
            .ToListAsync(cancellationToken);

        var stations = connectors
            .GroupBy(connector => connector.StationId)
            .Select(group => new StationAvailabilityResponse(
                group.Key,
                group.Select(connector => new ConnectorAvailabilityResponse(connector.PublicId, connector.Available))
                    .ToArray()))
            .ToArray();
        return Result.Ok(new StationAvailabilityListResponse(stations));
    }
}
