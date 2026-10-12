using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using FluentResults;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Features.Reservations;

public sealed record GetBookingPolicyRequest : IAuthenticatedRequest<BookingPolicyResponse>;

public sealed record BookingPolicyResponse(
    int MaximumOutstandingReservationsPerDriver,
    double MaximumBookingDurationSeconds,
    double MaximumAdvanceBookingHorizonSeconds);

public sealed class GetBookingPolicyQueryHandler(IOptionsSnapshot<ReservationPolicyOptions> options)
{
    public Result<BookingPolicyResponse> Handle(GetBookingPolicyRequest request) =>
        Result.Ok(options.Value.ToResponse());
}

internal static class BookingPolicyMappings
{
    public static BookingPolicyResponse ToResponse(this ReservationPolicyOptions options) => new(
        options.MaximumOutstandingReservationsPerDriver,
        options.MaximumBookingDuration.TotalSeconds,
        options.MaximumAdvanceBookingHorizon.TotalSeconds);
}
