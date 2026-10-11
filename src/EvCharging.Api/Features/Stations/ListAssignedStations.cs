using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Pagination;
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
        if (CursorPagination.ValidateLimit(request.Limit, nameof(request.Limit)) is { } error)
        {
            return Result.Fail<AssignedStationListResponse>(error);
        }

        var identity = user.GetUserIdentity();
        if (identity is null)
        {
            return Result.Fail<AssignedStationListResponse>(new ForbiddenError());
        }

        var stations = database.Stations
            .AsNoTracking()
            .Where(station => database.OperatorAssignments.Any(assignment => assignment.StationId == station.Id
                && database.OperatorUsers.Any(operatorUser => operatorUser.Id == assignment.OperatorUserId
                    && operatorUser.IdentityIssuer == identity.Issuer
                    && operatorUser.IdentitySubject == identity.Subject)));
        Guid afterId = Guid.Empty;
        if (request.AfterId is { } cursor)
        {
            var anchorId = await stations
                .Where(station => station.PublicId == cursor)
                .Select(station => (Guid?)station.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (anchorId is null)
            {
                return Result.Fail<AssignedStationListResponse>(new ValidationError(
                    nameof(request.AfterId), "The cursor does not identify an assigned station."));
            }

            afterId = anchorId.Value;
        }

        var page = await stations
            .Where(station => station.Id.CompareTo(afterId) > 0)
            .OrderBy(station => station.Id)
            .Take(request.Limit + 1)
            .Select(station => new AssignedStationResponse(station.PublicId, station.Name, station.Address))
            .ToListAsync(cancellationToken);
        var nextAfterId = CursorPagination.TrimPage(page, request.Limit, station => station.Id);

        return Result.Ok(new AssignedStationListResponse(page, nextAfterId));
    }
}
