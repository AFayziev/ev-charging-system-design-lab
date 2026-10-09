using EvCharging.Domain.Observations;
using Xunit;

namespace EvCharging.Domain.Tests.Observations;

public sealed class FreshnessPolicyTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_ReprocessedObservation_DoesNotRefreshSourceTime()
    {
        TimeSpan maximumAge = TimeSpan.FromMinutes(2);

        Assert.Equal(
            ObservationFreshness.Fresh, FreshnessPolicy.Evaluate(OccurredAt, OccurredAt.AddMinutes(2), maximumAge));
        Assert.Equal(
            ObservationFreshness.Stale,
            FreshnessPolicy.Evaluate(OccurredAt, OccurredAt.AddMinutes(2).AddTicks(1), maximumAge));
    }

    [Fact]
    public void Evaluate_MissingOrInvalidSourceTime_CannotEstablishFreshness()
    {
        TimeSpan maximumAge = TimeSpan.FromMinutes(2);

        Assert.Equal(ObservationFreshness.Unknown, FreshnessPolicy.Evaluate(null, OccurredAt, maximumAge));
        Assert.Equal(
            ObservationFreshness.Unknown,
            FreshnessPolicy.Evaluate(OccurredAt.AddMinutes(1), OccurredAt, maximumAge));
    }
}
