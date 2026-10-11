using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Reservations;

public sealed class BookingIntervalTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(BookingBoundary.Start, false)]
    [InlineData(BookingBoundary.Start, true)]
    [InlineData(BookingBoundary.End, false)]
    [InlineData(BookingBoundary.End, true)]
    public void Create_InfiniteBoundary_RejectsInterval(BookingBoundary boundary, bool maximum)
    {
        var infinite = maximum ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue;
        var start = boundary == BookingBoundary.Start ? infinite : Start;
        var end = boundary == BookingBoundary.End ? infinite : Start.AddHours(1);

        var result = BookingInterval.Create(start, end);

        Assert.True(result.IsFailed);
        Assert.Contains(
            result.Errors,
            error => error is BookingValidationError validation && validation.Boundary == boundary);
    }
}
