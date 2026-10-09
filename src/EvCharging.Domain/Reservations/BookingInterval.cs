namespace EvCharging.Domain.Reservations;

/// <summary>A nonempty UTC booking interval with an inclusive start and exclusive end.</summary>
public sealed record BookingInterval
{
    public BookingInterval(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("Booking end must follow its start.", nameof(end));
        }

        Start = start.ToUniversalTime();
        End = end.ToUniversalTime();
    }

    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public TimeSpan Duration => End - Start;

    public bool Contains(DateTimeOffset instant) => Start <= instant && instant < End;

    public bool Overlaps(BookingInterval other) => Start < other.End && other.Start < End;
}
