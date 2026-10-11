using FluentResults;

namespace EvCharging.Domain.Reservations;

public enum BookingBoundary
{
    Start,
    End
}

public sealed class BookingValidationError(BookingBoundary boundary, string message) : Error(message)
{
    public BookingBoundary Boundary { get; } = boundary;
}
