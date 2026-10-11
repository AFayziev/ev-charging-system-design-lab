namespace EvCharging.Api.Configuration;

internal sealed class ReservationPolicyOptions
{
    public int MaximumOutstandingReservationsPerDriver { get; set; }
    public TimeSpan MaximumBookingDuration { get; set; }
    public TimeSpan MaximumAdvanceBookingHorizon { get; set; }
    public TimeSpan ArrivalAllowance { get; set; }
}
