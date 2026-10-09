using EvCharging.Domain.Measurements;
using EvCharging.Domain.Reservations;

namespace EvCharging.Domain.Charging;

public static class SessionPolicy
{
    public static bool ClaimsConnector(SessionLifecycle lifecycle) =>
        lifecycle is SessionLifecycle.Starting or SessionLifecycle.Active or SessionLifecycle.Stopping;

    public static StartResolution ResolveStart(
        BookingInterval slot,
        DateTimeOffset now,
        CommandOutcome outcome,
        bool externalAttemptAuthorized)
    {
        if (outcome == CommandOutcome.Confirmed)
        {
            return new(SessionLifecycle.Active, ReservationState.Consumed, outcome);
        }

        bool expired = now >= slot.End;
        if (outcome == CommandOutcome.Rejected || (expired && !externalAttemptAuthorized))
        {
            return new(
                SessionLifecycle.Failed,
                expired ? ReservationState.Expired : ReservationState.Active,
                CommandOutcome.Rejected);
        }

        return new(SessionLifecycle.Starting, ReservationState.Active, outcome);
    }

    public static SessionLifecycle AcceptStop(SessionLifecycle current) =>
        current == SessionLifecycle.Active ? SessionLifecycle.Stopping : current;

    public static SessionLifecycle ResolveStop(SessionLifecycle current, CommandOutcome outcome) =>
        current == SessionLifecycle.Stopping && outcome == CommandOutcome.Rejected
            ? SessionLifecycle.Active
            : current;

    public static SessionProgress Complete(
        SessionProgress current,
        TerminalEvidence? terminal,
        EnergyReading? finalUsage,
        EnergyReading? latestUsage)
    {
        if (current.Lifecycle is not (SessionLifecycle.Active or SessionLifecycle.Stopping)
            || current.ProviderSession is null
            || terminal is null
            || !terminal.IsAuthoritative
            || terminal.ProviderSession != current.ProviderSession
            || finalUsage is null
            || !finalUsage.IsFinal
            || !UsagePolicy.IsValidTotal(current.ProviderSession, finalUsage, latestUsage))
        {
            return current;
        }

        return current with { Lifecycle = SessionLifecycle.Completed, FinalUsage = finalUsage };
    }
}
