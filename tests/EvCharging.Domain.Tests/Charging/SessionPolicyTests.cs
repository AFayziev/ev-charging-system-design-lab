using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Measurements;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Charging;

public sealed class SessionPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProviderIdentity ProviderSession = new(Guid.NewGuid(), "session-1");
    private static readonly BookingInterval Slot = BookingInterval.Create(Start, Start.AddHours(1)).Value;

    [Theory]
    [InlineData(CommandOutcome.Pending)]
    [InlineData(CommandOutcome.Uncertain)]
    public void ResolveStart_AuthorizedUnresolvedAttempt_RetainsClaimAfterExpiry(CommandOutcome outcome)
    {
        StartResolution result = SessionPolicy.ResolveStart(Slot, Slot.End, outcome, true);

        Assert.Equal(SessionLifecycle.Starting, result.Lifecycle);
        Assert.Equal(outcome, result.Outcome);
        Assert.True(SessionPolicy.ClaimsConnector(result.Lifecycle));
    }

    [Fact]
    public void ResolveStart_ExpiredBeforeAttempt_FailsAndReleases()
    {
        StartResolution result = SessionPolicy.ResolveStart(Slot, Slot.End, CommandOutcome.Pending, false);

        Assert.Equal(ReservationState.Expired, result.ReservationState);
        Assert.Equal(CommandOutcome.Rejected, result.Outcome);
        Assert.Equal(SessionLifecycle.Failed, result.Lifecycle);
        Assert.False(SessionPolicy.ClaimsConnector(result.Lifecycle));
    }

    [Theory]
    [InlineData(30, ReservationState.Active)]
    [InlineData(60, ReservationState.Expired)]
    public void ResolveStart_DefinitiveRejection_ReleasesClaim(int minutes, ReservationState expected)
    {
        StartResolution result = SessionPolicy.ResolveStart(
            Slot, Start.AddMinutes(minutes), CommandOutcome.Rejected, true);

        Assert.Equal(expected, result.ReservationState);
        Assert.Equal(SessionLifecycle.Failed, result.Lifecycle);
    }

    [Fact]
    public void ResolveStart_LateConfirmation_ConsumesReservation()
    {
        StartResolution result = SessionPolicy.ResolveStart(
            Slot, Slot.End.AddMinutes(5), CommandOutcome.Confirmed, true);

        Assert.Equal(ReservationState.Consumed, result.ReservationState);
        Assert.Equal(SessionLifecycle.Active, result.Lifecycle);
        Assert.True(SessionPolicy.ClaimsConnector(result.Lifecycle));
    }

    [Theory]
    [InlineData(CommandOutcome.Pending)]
    [InlineData(CommandOutcome.Uncertain)]
    [InlineData(CommandOutcome.Rejected)]
    public void ApplyEvidence_ConfirmedCommand_DoesNotRegress(CommandOutcome delayed)
    {
        Assert.Equal(CommandOutcome.Confirmed, CommandPolicy.ApplyEvidence(CommandOutcome.Confirmed, delayed));
    }

    [Theory]
    [InlineData(CommandOutcome.Pending)]
    [InlineData(CommandOutcome.Uncertain)]
    [InlineData(CommandOutcome.Confirmed)]
    public void ApplyEvidence_RejectedCommand_RemainsDefinitive(CommandOutcome delayed)
    {
        Assert.Equal(CommandOutcome.Rejected, CommandPolicy.ApplyEvidence(CommandOutcome.Rejected, delayed));
    }

    [Fact]
    public void ApplyEvidence_DelayedAcknowledgement_PreservesUncertainty()
    {
        Assert.Equal(
            CommandOutcome.Uncertain,
            CommandPolicy.ApplyEvidence(CommandOutcome.Uncertain, CommandOutcome.Pending));
        Assert.Equal(
            CommandOutcome.Confirmed,
            CommandPolicy.ApplyEvidence(CommandOutcome.Uncertain, CommandOutcome.Confirmed));
        Assert.Equal(
            CommandOutcome.Rejected,
            CommandPolicy.ApplyEvidence(CommandOutcome.Uncertain, CommandOutcome.Rejected));
    }

    [Theory]
    [InlineData(CommandOutcome.Pending, SessionLifecycle.Stopping)]
    [InlineData(CommandOutcome.Uncertain, SessionLifecycle.Stopping)]
    [InlineData(CommandOutcome.Confirmed, SessionLifecycle.Stopping)]
    [InlineData(CommandOutcome.Rejected, SessionLifecycle.Active)]
    public void ResolveStop_CommandOutcome_RequiresFinalization(CommandOutcome outcome, SessionLifecycle expected)
    {
        SessionLifecycle stopping = SessionPolicy.AcceptStop(SessionLifecycle.Active);

        Assert.Equal(expected, SessionPolicy.ResolveStop(stopping, outcome));
        Assert.True(SessionPolicy.ClaimsConnector(expected));
    }

    [Theory]
    [InlineData(SessionLifecycle.Active)]
    [InlineData(SessionLifecycle.Stopping)]
    public void Complete_MatchingTerminalAndUsage_FreezesFacts(SessionLifecycle state)
    {
        var session = new SessionProgress(state, ProviderSession);
        EnergyReading usage = Reading(5m, true);
        var terminal = new TerminalEvidence(ProviderSession, true);

        SessionProgress completed = SessionPolicy.Complete(session, terminal, usage, Reading(4m));

        Assert.Equal(SessionLifecycle.Completed, completed.Lifecycle);
        Assert.Equal(usage, completed.FinalUsage);
        Assert.False(SessionPolicy.ClaimsConnector(completed.Lifecycle));
        Assert.Equal(completed, SessionPolicy.Complete(completed, terminal, Reading(8m, true), usage));
    }

    [Fact]
    public void Complete_EvidenceArrivesSeparately_WaitsForBoth()
    {
        var session = new SessionProgress(SessionLifecycle.Stopping, ProviderSession);
        EnergyReading final = Reading(5m, true);
        var terminal = new TerminalEvidence(ProviderSession, true);

        Assert.Equal(session, SessionPolicy.Complete(session, terminal, null, null));
        Assert.Equal(session, SessionPolicy.Complete(session, null, final, null));
        Assert.Equal(SessionLifecycle.Completed, SessionPolicy.Complete(session, terminal, final, null).Lifecycle);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Complete_HistoricalFinalUsage_FillsMissingFact(int finalEnergy)
    {
        var session = new SessionProgress(SessionLifecycle.Stopping, ProviderSession);
        var terminal = new TerminalEvidence(ProviderSession, true);
        EnergyReading latest = new(
            ProviderSession, 4m, EnergyMeaning.SessionCumulative, Start.AddMinutes(1), false);
        EnergyReading final = Reading(finalEnergy, true);

        SessionProgress completed = SessionPolicy.Complete(session, terminal, final, latest);

        Assert.Equal(SessionLifecycle.Completed, completed.Lifecycle);
        Assert.Equal(final, completed.FinalUsage);
        Assert.Equal(latest, UsagePolicy.AcceptTotal(ProviderSession, latest, final));
    }

    [Fact]
    public void Complete_MismatchedOrUnauthoritativeTerminal_RetainsClaim()
    {
        var session = new SessionProgress(SessionLifecycle.Stopping, ProviderSession);
        var other = new ProviderIdentity(Guid.NewGuid(), ProviderSession.ExternalId);

        Assert.Equal(session, SessionPolicy.Complete(session, new(other, true), Reading(5m, true), null));
        Assert.Equal(session, SessionPolicy.Complete(session, new(ProviderSession, false), Reading(5m, true), null));
    }

    [Fact]
    public void Complete_InvalidFinalUsage_RetainsClaim()
    {
        var session = new SessionProgress(SessionLifecycle.Stopping, ProviderSession);
        var terminal = new TerminalEvidence(ProviderSession, true);
        var wrongSession = new ProviderIdentity(ProviderSession.ProviderId, "session-2");
        EnergyReading[] invalid =
        [
            Reading(5m),
            Reading(-1m, true),
            Reading(3m, true),
            new(wrongSession, 5m, EnergyMeaning.SessionCumulative, Start, true),
            new(ProviderSession, 5m, EnergyMeaning.DeviceLifetime, Start, true),
            new(ProviderSession, 5m, EnergyMeaning.Incremental, Start, true)
        ];

        foreach (EnergyReading reading in invalid)
        {
            Assert.Equal(session, SessionPolicy.Complete(session, terminal, reading, Reading(4m)));
        }
    }

    [Theory]
    [InlineData(SessionLifecycle.Starting)]
    [InlineData(SessionLifecycle.Failed)]
    public void Complete_UnestablishedSession_DoesNotComplete(SessionLifecycle state)
    {
        var session = new SessionProgress(state, ProviderSession);

        Assert.Equal(session, SessionPolicy.Complete(session, new(ProviderSession, true), Reading(5m, true), null));
    }

    [Fact]
    public void AcceptTotal_Snapshots_UsesLatestValidTotal()
    {
        EnergyReading latest = Reading(4m);
        EnergyReading next = new(ProviderSession, 5m, EnergyMeaning.SessionCumulative, Start.AddMinutes(1), false);

        EnergyReading? accepted = UsagePolicy.AcceptTotal(ProviderSession, latest, next);

        Assert.Equal(5m, accepted?.EnergyKWh);
        Assert.Equal(next, UsagePolicy.AcceptTotal(ProviderSession, accepted, Reading(3m)));
        EnergyReading oldHigher = Reading(6m);
        Assert.Equal(next, UsagePolicy.AcceptTotal(ProviderSession, accepted, oldHigher));
    }

    private static EnergyReading Reading(decimal energyKWh, bool isFinal = false) =>
        new(ProviderSession, energyKWh, EnergyMeaning.SessionCumulative, Start, isFinal);
}
