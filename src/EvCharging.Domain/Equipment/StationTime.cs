namespace EvCharging.Domain.Equipment;

public static class StationTime
{
    /// <summary>Interprets a wall time with an explicit offset in the station's named zone.</summary>
    public static DateTimeOffset ToUtc(DateTimeOffset localTime, string timeZoneId)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        DateTime wallTime = localTime.DateTime;
        if (zone.IsInvalidTime(wallTime))
        {
            throw new ArgumentException("The station wall time does not exist.", nameof(localTime));
        }

        bool validOffset = zone.IsAmbiguousTime(wallTime)
            ? zone.GetAmbiguousTimeOffsets(wallTime).Contains(localTime.Offset)
            : zone.GetUtcOffset(wallTime) == localTime.Offset;
        if (!validOffset)
        {
            throw new ArgumentException("The offset does not match the station time zone.", nameof(localTime));
        }

        return localTime.ToUniversalTime();
    }
}
