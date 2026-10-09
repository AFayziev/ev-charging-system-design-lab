using EvCharging.Domain.Equipment;
using Xunit;

namespace EvCharging.Domain.Tests.Equipment;

public sealed class StationTimeTests
{
    [Fact]
    public void ToUtc_AmbiguousBooking_RequiresMatchingExplicitOffset()
    {
        var earlier = new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-4));
        var later = earlier.ToOffset(TimeSpan.FromHours(-5)).AddHours(1);

        DateTimeOffset first = StationTime.ToUtc(earlier, "America/New_York");
        DateTimeOffset second = StationTime.ToUtc(later, "America/New_York");

        Assert.Equal(TimeSpan.FromHours(1), second - first);
        Assert.Throws<ArgumentException>(() =>
            StationTime.ToUtc(new DateTimeOffset(earlier.DateTime, TimeSpan.FromHours(-6)), "America/New_York"));
    }

    [Fact]
    public void ToUtc_MissingSpringHour_RejectsBooking()
    {
        var local = new DateTimeOffset(2026, 3, 8, 2, 30, 0, TimeSpan.FromHours(-5));

        Assert.Throws<ArgumentException>(() => StationTime.ToUtc(local, "America/New_York"));
    }

    [Fact]
    public void ToUtc_OrdinaryBooking_ValidatesNamedZone()
    {
        var local = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(5));

        Assert.Equal(
            new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero), StationTime.ToUtc(local, "Asia/Tashkent"));
        Assert.Throws<ArgumentException>(() => StationTime.ToUtc(local, "America/New_York"));
    }
}
