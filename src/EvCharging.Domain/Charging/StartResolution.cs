using EvCharging.Domain.Reservations;

namespace EvCharging.Domain.Charging;

public sealed record StartResolution(
    SessionLifecycle Lifecycle,
    ReservationState ReservationState,
    CommandOutcome Outcome);
