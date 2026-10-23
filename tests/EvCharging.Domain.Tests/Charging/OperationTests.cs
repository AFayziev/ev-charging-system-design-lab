using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Charging;

public sealed class OperationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ScheduleAutomaticChecks_BeforeEffectAuthorization_ReturnsFailureWithoutMutation()
    {
        var operation = Operation.AcceptStart(ChargingSession.AcceptStart(Booking(), Start).Value, Guid.NewGuid());

        var result = operation.ScheduleAutomaticChecks(Start.AddSeconds(35));

        Assert.True(result.IsFailed);
        Assert.Null(operation.AutomaticChecksEndAt);
        Assert.Equal(CommandOutcome.Pending, operation.Outcome);
        Assert.Equal(0, operation.AuthorizedAttemptCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfirmStart_MatchingEvidence_ConsumesReservationAndCannotRegress(bool initiallyUncertain)
    {
        var reservation = Booking();
        var session = ChargingSession.AcceptStart(reservation, Start).Value;
        var operation = Operation.AcceptStart(session, Guid.NewGuid());
        if (initiallyUncertain)
        {
            Assert.True(operation.MarkUncertain().IsSuccess);
            Assert.Equal(SessionLifecycle.Starting, session.Lifecycle);
            Assert.Equal(ReservationState.Active, reservation.State);
        }

        var providerId = Guid.NewGuid();
        Assert.True(operation.ConfirmStart(providerId, "provider-session", Start.AddSeconds(1)).IsSuccess);
        Assert.Equal(CommandOutcome.Confirmed, operation.Outcome);
        Assert.Equal(SessionLifecycle.Active, session.Lifecycle);
        Assert.Equal(ReservationState.Consumed, reservation.State);
        Assert.Equal(providerId, session.ChargingProviderId);
        Assert.Equal(Start.AddSeconds(1), session.StartedAt);
        Assert.Equal(
            ChargingStartError.OperationResolvedCode,
            Assert.IsType<ChargingStartError>(Assert.Single(operation.MarkUncertain().Errors)).Code);
        Assert.Equal(
            ChargingStartError.OperationResolvedCode,
            Assert.IsType<ChargingStartError>(Assert.Single(operation.RejectStart().Errors)).Code);
        Assert.Equal(CommandOutcome.Confirmed, operation.Outcome);
        Assert.Equal(SessionLifecycle.Active, session.Lifecycle);
    }

    [Fact]
    public void RejectStart_DefinitiveOutcome_FailsSessionWithoutConsumingReservation()
    {
        var reservation = Booking();
        var session = ChargingSession.AcceptStart(reservation, Start).Value;
        var operation = Operation.AcceptStart(session, Guid.NewGuid());

        Assert.True(operation.RejectStart().IsSuccess);

        Assert.Equal(CommandOutcome.Rejected, operation.Outcome);
        Assert.Equal(SessionLifecycle.Failed, session.Lifecycle);
        Assert.Equal(ReservationState.Active, reservation.State);
        var result = operation.ConfirmStart(Guid.NewGuid(), "late", Start);
        Assert.Equal(
            ChargingStartError.OperationResolvedCode,
            Assert.IsType<ChargingStartError>(Assert.Single(result.Errors)).Code);
        Assert.Equal(SessionLifecycle.Failed, session.Lifecycle);
    }

    [Fact]
    public void ConfirmStart_InactiveReservation_LeavesOperationAndSessionUnchanged()
    {
        var reservation = Booking();
        var session = ChargingSession.AcceptStart(reservation, Start).Value;
        var operation = Operation.AcceptStart(session, Guid.NewGuid());
        Assert.True(reservation.Cancel(Start, false).IsSuccess);

        var result = operation.ConfirmStart(Guid.NewGuid(), "provider-session", Start);

        Assert.Equal(
            ChargingStartError.ReservationInactiveCode,
            Assert.IsType<ChargingStartError>(Assert.Single(result.Errors)).Code);
        Assert.Equal(CommandOutcome.Pending, operation.Outcome);
        Assert.Equal(SessionLifecycle.Starting, session.Lifecycle);
        Assert.Null(session.ChargingProviderId);
        Assert.Null(session.ProviderSessionId);
        Assert.Null(session.StartedAt);
        Assert.Equal(ReservationState.Cancelled, reservation.State);
    }

    [Fact]
    public void AcceptStop_ActiveSession_MovesSessionToStoppingWithPendingStop()
    {
        var session = ActiveSession();
        var key = Guid.NewGuid();

        var result = Operation.AcceptStop(session, key, Start.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(ChargingCommandKind.Stop, result.Value.Kind);
        Assert.Equal(CommandOutcome.Pending, result.Value.Outcome);
        Assert.Equal(key, result.Value.IdempotencyKey);
        Assert.Equal(Start.AddMinutes(5), result.Value.AcceptedAt);
        Assert.Equal(session.Reservation!.DriverId, result.Value.DriverId);
        Assert.Equal(SessionLifecycle.Stopping, session.Lifecycle);
    }

    [Theory]
    [InlineData(SessionLifecycle.Starting, ChargingStopError.SessionNotActiveCode)]
    [InlineData(SessionLifecycle.Stopping, ChargingStopError.StopInProgressCode)]
    public void AcceptStop_SessionNotActive_ReturnsConflictWithoutMutation(SessionLifecycle lifecycle, string code)
    {
        var session = lifecycle switch
        {
            SessionLifecycle.Starting => ChargingSession.AcceptStart(Booking(), Start).Value,
            _ => StoppingSession()
        };

        var result = Operation.AcceptStop(session, Guid.NewGuid(), Start.AddMinutes(5));

        Assert.Equal(code, Assert.IsType<ChargingStopError>(Assert.Single(result.Errors)).Code);
        Assert.Equal(lifecycle, session.Lifecycle);
    }

    [Fact]
    public void ConfirmStop_UnresolvedStop_RecordsOutcomeAndKeepsSessionStopping()
    {
        var session = ActiveSession();
        var stop = Operation.AcceptStop(session, Guid.NewGuid(), Start.AddMinutes(5)).Value;
        Assert.True(stop.MarkUncertain().IsSuccess);

        Assert.True(stop.ConfirmStop().IsSuccess);

        Assert.Equal(CommandOutcome.Confirmed, stop.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, session.Lifecycle);
        Assert.Equal(
            ChargingStopError.OperationResolvedCode,
            Assert.IsType<ChargingStopError>(Assert.Single(stop.RejectStop().Errors)).Code);
        Assert.Equal(
            ChargingStopError.OperationResolvedCode,
            Assert.IsType<ChargingStopError>(Assert.Single(stop.MarkUncertain().Errors)).Code);
        Assert.Equal(CommandOutcome.Confirmed, stop.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, session.Lifecycle);
    }

    [Fact]
    public void RejectStop_DefinitiveOutcome_ReturnsSessionToActiveAndAllowsAnotherStop()
    {
        var session = ActiveSession();
        var stop = Operation.AcceptStop(session, Guid.NewGuid(), Start.AddMinutes(5)).Value;

        Assert.True(stop.RejectStop().IsSuccess);

        Assert.Equal(CommandOutcome.Rejected, stop.Outcome);
        Assert.Equal(SessionLifecycle.Active, session.Lifecycle);
        Assert.Equal(
            ChargingStopError.OperationResolvedCode,
            Assert.IsType<ChargingStopError>(Assert.Single(stop.ConfirmStop().Errors)).Code);
        Assert.True(Operation.AcceptStop(session, Guid.NewGuid(), Start.AddMinutes(6)).IsSuccess);
        Assert.Equal(SessionLifecycle.Stopping, session.Lifecycle);
    }

    [Fact]
    public void RejectStop_SessionAlreadyCompleted_SettlesTheStopAndKeepsTheSessionCompleted()
    {
        var session = ActiveSession();
        var stop = Operation.AcceptStop(session, Guid.NewGuid(), Start.AddMinutes(5)).Value;
        Assert.True(stop.MarkUncertain().IsSuccess);
        Assert.True(session.Complete(7.25m, Start.AddMinutes(6)).IsSuccess);

        Assert.True(stop.RejectStop().IsSuccess);

        Assert.Equal(CommandOutcome.Rejected, stop.Outcome);
        Assert.Equal(SessionLifecycle.Completed, session.Lifecycle);
        Assert.Equal(7.25m, session.FinalEnergyKWh);
    }

    [Fact]
    public void StartOutcomes_AppliedToAStop_AreRefused()
    {
        var session = ActiveSession();
        var stop = Operation.AcceptStop(session, Guid.NewGuid(), Start.AddMinutes(5)).Value;

        var result = stop.ConfirmStart(Guid.NewGuid(), "provider-session", Start.AddMinutes(6));

        Assert.Equal(
            ChargingStartError.OperationResolvedCode,
            Assert.IsType<ChargingStartError>(Assert.Single(result.Errors)).Code);
        Assert.Equal(CommandOutcome.Pending, stop.Outcome);
        Assert.Equal(SessionLifecycle.Stopping, session.Lifecycle);
    }

    [Theory]
    [InlineData(SessionLifecycle.Active)]
    [InlineData(SessionLifecycle.Stopping)]
    public void Complete_ActiveOrStoppingSession_FreezesFinalUsageAndEndTime(SessionLifecycle lifecycle)
    {
        var session = lifecycle == SessionLifecycle.Active ? ActiveSession() : StoppingSession();

        var result = session.Complete(7.25m, Start.AddMinutes(9));

        Assert.True(result.IsSuccess);
        Assert.Equal(SessionLifecycle.Completed, session.Lifecycle);
        Assert.Equal(7.25m, session.FinalEnergyKWh);
        Assert.Equal(Start.AddMinutes(9), session.CompletedAt);
    }

    [Theory]
    [InlineData(SessionLifecycle.Starting)]
    [InlineData(SessionLifecycle.Failed)]
    [InlineData(SessionLifecycle.Completed)]
    public void Complete_SessionNotActiveOrStopping_ReturnsFailureWithoutMutation(SessionLifecycle lifecycle)
    {
        var session = ChargingSession.AcceptStart(Booking(), Start).Value;
        if (lifecycle == SessionLifecycle.Failed)
        {
            Assert.True(session.RejectStart().IsSuccess);
        }
        else if (lifecycle == SessionLifecycle.Completed)
        {
            session = ActiveSession();
            Assert.True(session.Complete(7.25m, Start.AddMinutes(9)).IsSuccess);
        }

        var result = session.Complete(9m, Start.AddMinutes(10));

        Assert.Equal(
            ChargingCompletionError.SessionNotCompletableCode,
            Assert.IsType<ChargingCompletionError>(Assert.Single(result.Errors)).Code);
        Assert.Equal(lifecycle, session.Lifecycle);
        Assert.Equal(lifecycle == SessionLifecycle.Completed ? 7.25m : null, session.FinalEnergyKWh);
    }

    private static ChargingSession ActiveSession()
    {
        var session = ChargingSession.AcceptStart(Booking(), Start).Value;
        var start = Operation.AcceptStart(session, Guid.NewGuid());
        Assert.True(start.ConfirmStart(Guid.NewGuid(), "provider-session", Start.AddSeconds(1)).IsSuccess);
        return session;
    }

    private static ChargingSession StoppingSession()
    {
        var session = ActiveSession();
        Assert.True(Operation.AcceptStop(session, Guid.NewGuid(), Start.AddMinutes(1)).IsSuccess);
        return session;
    }

    private static Reservation Booking() => Reservation.Book(
        new Connector { Id = Guid.NewGuid(), ConnectorType = "Type2", PowerSupply = "AC" },
        Driver.Register("https://test.invalid", "driver", EmailAddress.Create("driver@example.test").Value),
        Start,
        Start.AddHours(1),
        Start,
        new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
}
