using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Pagination;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Reservations;

internal sealed record ListReservationsRequest(Guid? AfterId = null, int Limit = CursorPagination.DefaultPageSize);
internal sealed record ReservationListResponse(IReadOnlyList<ReservationResponse> Reservations, Guid? NextAfterId);

internal sealed class ListReservationsQueryHandler(PlatformDbContext database)
{
    public async Task<Result<ReservationListResponse>> HandleAsync(
        ListReservationsRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identity = user.GetUserIdentity();
        if (identity is null)
        {
            return Result.Fail<ReservationListResponse>(new ForbiddenError());
        }

        var owned = database.Reservations
            .AsNoTracking()
            .OwnedBy(identity);
        if (CursorPagination.ValidateLimit(request.Limit, nameof(request.Limit)) is { } error)
        {
            return Result.Fail<ReservationListResponse>(error);
        }

        var pageQuery = await ApplyCursorAsync(owned, request.AfterId, cancellationToken);
        if (pageQuery.IsFailed)
        {
            return Result.Fail<ReservationListResponse>(pageQuery.Errors);
        }

        return Result.Ok(await GetPageAsync(pageQuery.Value, request.Limit, cancellationToken));
    }

    private static async Task<Result<IQueryable<Reservation>>> ApplyCursorAsync(
        IQueryable<Reservation> reservations, Guid? afterId, CancellationToken cancellationToken)
    {
        if (afterId is null)
        {
            return Result.Ok(reservations);
        }

        var anchor = await reservations
            .Where(reservation => reservation.PublicId == afterId)
            .Select(reservation => new { reservation.Id, reservation.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (anchor is null)
        {
            return Result.Fail<IQueryable<Reservation>>(
                new ValidationError(nameof(ListReservationsRequest.AfterId), "Invalid reservation cursor."));
        }

        return Result.Ok(reservations.Where(reservation => reservation.CreatedAt < anchor.CreatedAt ||
            (reservation.CreatedAt == anchor.CreatedAt && reservation.Id.CompareTo(anchor.Id) < 0)));
    }

    private static async Task<ReservationListResponse> GetPageAsync(
        IQueryable<Reservation> pageQuery, int limit, CancellationToken cancellationToken)
    {
        var reservations = await pageQuery
            .OrderByDescending(reservation => reservation.CreatedAt)
            .ThenByDescending(reservation => reservation.Id)
            .Take(limit + 1)
            .WithEquipment()
            .ToListAsync(cancellationToken);
        var nextAfterId = CursorPagination.TrimPage(reservations, limit, reservation => reservation.PublicId);

        return new ReservationListResponse(
            reservations.Select(reservation => reservation.ToResponse()).ToArray(),
            nextAfterId);
    }
}
