using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Pagination;
using EvCharging.Domain.Equipment;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Stations;

public sealed record ListAssignedStationsRequest(Guid? AfterId = null, int Limit = CursorPagination.DefaultPageSize)
    : IAuthenticatedRequest<AssignedStationListResponse>;
public sealed record AssignedStationResponse(Guid Id, string Name, string Address);
public sealed record AssignedStationListResponse(IReadOnlyList<AssignedStationResponse> Stations, Guid? NextAfterId);

public sealed class ListAssignedStationsQueryHandler(
    PlatformDbContext database)
{
    public async Task<Result<AssignedStationListResponse>> HandleAsync(
        ListAssignedStationsRequest request, UserIdentity identity, CancellationToken cancellationToken)
    {
        if (CursorPagination.ValidateLimit(request.Limit, nameof(request.Limit)) is { } error)
        {
            return Result.Fail<AssignedStationListResponse>(error);
        }

        var stations = database.Stations
            .AsNoTracking()
            .Where(station => database.OperatorAssignments.Any(assignment => assignment.StationId == station.Id
                && database.OperatorUsers.Any(operatorUser => operatorUser.Id == assignment.OperatorUserId
                    && operatorUser.IdentityIssuer == identity.Issuer
                    && operatorUser.IdentitySubject == identity.Subject)));
        var cursor = await ResolveCursorAsync(stations, request.AfterId, cancellationToken);
        if (cursor.IsFailed)
        {
            return Result.Fail<AssignedStationListResponse>(cursor.Errors);
        }

        return Result.Ok(await GetPageAsync(stations, cursor.Value, request.Limit, cancellationToken));
    }

    private static async Task<Result<Guid>> ResolveCursorAsync(
        IQueryable<Station> stations, Guid? afterId, CancellationToken cancellationToken)
    {
        if (afterId is null)
        {
            return Result.Ok(Guid.Empty);
        }

        var anchorId = await stations
            .Where(station => station.PublicId == afterId)
            .Select(station => (Guid?)station.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return anchorId is { } id
            ? Result.Ok(id)
            : Result.Fail<Guid>(new ValidationError(
                nameof(ListAssignedStationsRequest.AfterId), "The cursor does not identify an assigned station."));
    }

    private static async Task<AssignedStationListResponse> GetPageAsync(
        IQueryable<Station> stations, Guid afterId, int limit, CancellationToken cancellationToken)
    {
        var page = await stations
            .Where(station => station.Id.CompareTo(afterId) > 0)
            .OrderBy(station => station.Id)
            .Take(limit + 1)
            .Select(station => new AssignedStationResponse(station.PublicId, station.Name, station.Address))
            .ToListAsync(cancellationToken);
        var nextAfterId = CursorPagination.TrimPage(page, limit, station => station.Id);

        return new AssignedStationListResponse(page, nextAfterId);
    }
}
