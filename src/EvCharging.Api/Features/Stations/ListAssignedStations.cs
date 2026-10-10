using System.Security.Claims;
using EvCharging.Api.Errors;
using EvCharging.Api.Persistence;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

internal sealed record AssignedStationResponse(Guid Id, string Name, string Address);
internal sealed record AssignedStationListResponse(IReadOnlyList<AssignedStationResponse> Stations, Guid? NextAfterId);

internal sealed class ListAssignedStationsQueryHandler(
    [FromKeyedServices(PlatformPersistenceExtensions.WriteRoute)] PlatformDbContext database)
{
    public async Task<Result<AssignedStationListResponse>> HandleAsync(
        ListStationsRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (request.Limit is < 1 or > ListStationsRequest.MaximumPageSize)
        {
            return Result.Fail<AssignedStationListResponse>(
                new ValidationError(
                    nameof(request.Limit),
                    $"The limit must be between 1 and {ListStationsRequest.MaximumPageSize}."));
        }

        string? issuer = user.FindFirst("iss")?.Value;
        string? subject = user.FindFirst("sub")?.Value;
        var stations = database.Stations
            .AsNoTracking()
            .Where(station => database.OperatorAssignments.Any(assignment => assignment.StationId == station.Id
                && database.OperatorUsers.Any(operatorUser => operatorUser.Id == assignment.OperatorUserId
                    && operatorUser.IdentityIssuer == issuer && operatorUser.IdentitySubject == subject)));
        long afterId = 0;
        if (request.AfterId is { } cursor)
        {
            var anchorId = await stations
                .Where(station => station.PublicId == cursor)
                .Select(station => (long?)station.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (anchorId is null)
            {
                return Result.Fail<AssignedStationListResponse>(new ValidationError(
                    nameof(request.AfterId), "The cursor does not identify an assigned station."));
            }

            afterId = anchorId.Value;
        }

        var page = await stations
            .Where(station => station.Id > afterId)
            .OrderBy(station => station.Id)
            .Take(request.Limit + 1)
            .Select(station => new AssignedStationResponse(station.PublicId, station.Name, station.Address))
            .ToListAsync(cancellationToken);
        bool hasMore = page.Count > request.Limit;
        if (hasMore)
        {
            page.RemoveAt(request.Limit);
        }

        return Result.Ok(new AssignedStationListResponse(page, hasMore ? page[^1].Id : null));
    }
}
