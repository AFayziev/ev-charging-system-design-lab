using EvCharging.Api.Errors;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

internal sealed record ListStationsRequest(Guid? AfterId = null, int Limit = 20);

internal sealed record StationListResponse(IReadOnlyList<StationSummaryResponse> Stations, Guid? NextAfterId);

internal sealed record StationSummaryResponse(
    Guid Id,
    string Name,
    string Address,
    int ChargerCount,
    int EvseCount,
    int ConnectorCount,
    IReadOnlyList<ConnectorCapabilityResponse> Capabilities);

internal sealed record ConnectorCapabilityResponse(string ConnectorType, string PowerSupply, decimal RatedPowerKW);

internal sealed class ListStationsQueryHandler(PlatformDbContext database)
{
    private const int MaximumPageSize = 50;

    public async Task<Result<StationListResponse>> HandleAsync(
        ListStationsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        long afterId = 0;
        if (request.AfterId is { } cursor)
        {
            var anchorId = await database.Stations
                .Where(station => station.PublicId == cursor)
                .Select(station => (long?)station.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (anchorId is null)
            {
                errors.Add(new ValidationError(
                    nameof(request.AfterId), "The cursor does not identify an existing station."));
            }
            else
            {
                afterId = anchorId.Value;
            }
        }

        if (request.Limit is < 1 or > MaximumPageSize)
        {
            errors.Add(
                new ValidationError(nameof(request.Limit), $"The limit must be between 1 and {MaximumPageSize}."));
        }

        if (errors.Count > 0)
        {
            return Result.Fail<StationListResponse>(errors);
        }

        var stations = await database.Stations
            .AsNoTracking()
            .Where(station => station.Id > afterId)
            .OrderBy(station => station.Id)
            .Take(request.Limit + 1)
            .Select(
                station => new
                {
                    Station = station,
                    ChargerCount = station.Chargers.Count,
                    EvseCount = station.Chargers.SelectMany(charger => charger.Evses).Count(),
                    Connectors = station.Chargers
                        .SelectMany(charger => charger.Evses)
                        .SelectMany(evse => evse.Connectors)
                        .ToList()
                })
            .ToListAsync(cancellationToken);
        bool hasMore = stations.Count > request.Limit;
        if (hasMore)
        {
            stations.RemoveAt(request.Limit);
        }

        var summaries = stations
            .Select(item => item.Station.ToResponse(item.ChargerCount, item.EvseCount, item.Connectors))
            .ToArray();
        return Result.Ok(new StationListResponse(summaries, hasMore ? stations[^1].Station.PublicId : null));
    }
}
