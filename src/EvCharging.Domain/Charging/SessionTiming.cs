namespace EvCharging.Domain.Charging;

/// <summary>Domain rules for the timing of a charging session.</summary>
public static class SessionTiming
{
    /// <summary>
    /// Whether the provider has been silent for longer than <paramref name="limit"/>. A session that has reported
    /// nothing is silent since it started; one that has not started, has failed or is complete is not silent.
    /// </summary>
    public static bool IsSilentFor(
        SessionLifecycle lifecycle,
        DateTimeOffset? lastHeardAt,
        bool isFinalReading,
        DateTimeOffset now,
        TimeSpan limit) =>
        IsRunning(lifecycle) && !isFinalReading && lastHeardAt is { } heard && now - heard > limit;

    /// <summary>
    /// How long the session has run: up to now while it is live, up to its last report once the provider's final
    /// reading is in or the session has ended. Unknown before it starts, and for an ended session that never reported.
    /// </summary>
    public static TimeSpan? Elapsed(
        SessionLifecycle lifecycle,
        DateTimeOffset? startedAt,
        DateTimeOffset? lastReportedAt,
        bool isFinalReading,
        DateTimeOffset now)
    {
        if (startedAt is not { } started)
        {
            return null;
        }

        var live = lifecycle is SessionLifecycle.Starting || IsRunning(lifecycle);
        var end = live && !isFinalReading ? now : lastReportedAt;
        return end is { } finished ? TimeSpan.FromTicks(Math.Max(0, (finished - started).Ticks)) : null;
    }

    private static bool IsRunning(SessionLifecycle lifecycle) =>
        lifecycle is SessionLifecycle.Active or SessionLifecycle.Stopping;
}
