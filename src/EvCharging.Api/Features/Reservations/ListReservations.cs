using System.Security.Claims;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Pagination;
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

        if (request.AfterId is { } cursor)
        {
            var anchor = await owned
                .Where(reservation => reservation.PublicId == cursor)
                .Select(reservation => new { reservation.Id, reservation.CreatedAt })
                .SingleOrDefaultAsync(cancellationToken);
            if (anchor is null)
            {
                return Result.Fail<ReservationListResponse>(
                    new ValidationError(nameof(request.AfterId), "Invalid reservation cursor."));
            }

            owned = owned.Where(reservation => reservation.CreatedAt < anchor.CreatedAt ||
                (reservation.CreatedAt == anchor.CreatedAt && reservation.Id.CompareTo(anchor.Id) < 0));
        }

        var reservations = await owned
            .OrderByDescending(reservation => reservation.CreatedAt)
            .ThenByDescending(reservation => reservation.Id)
            .Take(request.Limit + 1)
            .WithEquipment()
            .ToListAsync(cancellationToken);
        var nextAfterId = CursorPagination.TrimPage(reservations, request.Limit, reservation => reservation.PublicId);

        return Result.Ok(new ReservationListResponse(
            reservations.Select(reservation => reservation.ToResponse()).ToArray(),
            nextAfterId));
    }
}
