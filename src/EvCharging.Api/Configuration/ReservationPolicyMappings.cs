using EvCharging.Domain.Reservations;

namespace EvCharging.Api.Configuration;

internal static class ReservationPolicyMappings
{
    public static ReservationBookingLimits ToDomain(this ReservationPolicyOptions options) => new(
        options.MaximumOutstandingReservationsPerDriver,
        options.MaximumBookingDuration,
        options.MaximumAdvanceBookingHorizon,
        options.ArrivalAllowance);
}
