using FluentResults;

namespace EvCharging.Domain.Reservations;

public sealed class ReservationCancellationError(string message) : Error(message);
