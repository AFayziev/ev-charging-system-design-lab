namespace EvCharging.Domain.Observations;

/// <summary>
/// What the provider's observations have established about one charging session. Every rule ignores a reading that
/// is already accounted for, so a duplicate or replayed reading changes nothing.
/// </summary>
public sealed class SessionEvidence
{
    private SessionEvidence()
    {
    }

    public Guid SessionId { get; private set; }
    public uint Version { get; private set; }

    /// <summary>Source occurrence time of the newest accepted reading.</summary>
    public DateTimeOffset? LastObservedAt { get; private set; }

    /// <summary>The session's cumulative energy reported by the provider's final reading.</summary>
    public decimal? FinalEnergyKWh { get; private set; }

    /// <summary>Source occurrence time of the final reading.</summary>
    public DateTimeOffset? FinalReadingAt { get; private set; }

    public static SessionEvidence Create(Guid sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        return new SessionEvidence { SessionId = sessionId };
    }

    /// <summary>
    /// Takes one reading into account. A reading dated beyond <paramref name="clockSkew"/> ahead of its receipt
    /// cannot outrank later readings, so it never advances <see cref="LastObservedAt"/>. Returns whether the reading
    /// supplied the final facts for the first time.
    /// </summary>
    public bool Record(
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        decimal energyKWh,
        bool isFinal,
        TimeSpan clockSkew)
    {
        // The store keeps microseconds, so readings are ranked at that precision however they are stored.
        var observed = new DateTimeOffset(
            occurredAt.UtcTicks / TimeSpan.TicksPerMicrosecond * TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        bool isCurrent = occurredAt <= receivedAt + clockSkew;
        if (isCurrent && (LastObservedAt is not { } last || observed > last))
        {
            LastObservedAt = observed;
        }

        // A final reading completes a missing fact even when it arrives after later readings or is dated ahead.
        if (isFinal && FinalEnergyKWh is null)
        {
            FinalEnergyKWh = energyKWh;
            FinalReadingAt = observed;
            return true;
        }

        return false;
    }
}
