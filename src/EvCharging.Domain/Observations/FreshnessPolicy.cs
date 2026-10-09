namespace EvCharging.Domain.Observations;

public static class FreshnessPolicy
{
    public static ObservationFreshness Evaluate(
        DateTimeOffset? occurredAt,
        DateTimeOffset now,
        TimeSpan maximumAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAge, TimeSpan.Zero);
        if (occurredAt is null || occurredAt > now)
        {
            return ObservationFreshness.Unknown;
        }

        return now - occurredAt.Value <= maximumAge ? ObservationFreshness.Fresh : ObservationFreshness.Stale;
    }
}
