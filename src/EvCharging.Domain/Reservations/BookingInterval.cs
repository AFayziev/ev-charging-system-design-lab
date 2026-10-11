using FluentResults;

namespace EvCharging.Domain.Reservations;

/// <summary>A nonempty UTC booking interval with an inclusive start and exclusive end.</summary>
public sealed record BookingInterval
{
    private BookingInterval(DateTimeOffset start, DateTimeOffset end)
    {
        Start = start.ToUniversalTime();
        End = end.ToUniversalTime();
    }

    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public TimeSpan Duration => End - Start;

    public static Result<BookingInterval> Create(DateTimeOffset start, DateTimeOffset end)
    {
        var errors = new List<BookingValidationError>();
        if (start == DateTimeOffset.MinValue || start == DateTimeOffset.MaxValue)
        {
            errors.Add(new(BookingBoundary.Start, "Choose a finite start instant."));
        }

        if (end == DateTimeOffset.MinValue || end == DateTimeOffset.MaxValue)
        {
            errors.Add(new(BookingBoundary.End, "Choose a finite end instant."));
        }

        if (end <= start)
        {
            errors.Add(new(BookingBoundary.End, "The end must follow the start."));
        }

        return errors.Count > 0
            ? Result.Fail<BookingInterval>(errors)
            : Result.Ok(new BookingInterval(start, end));
    }

    public bool Contains(DateTimeOffset instant) => Start <= instant && instant < End;

    public bool Overlaps(BookingInterval other) => Start < other.End && other.Start < End;
}
