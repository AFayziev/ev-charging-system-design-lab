using EvCharging.Domain.Charging;
using EvCharging.Domain.Equipment;
using EvCharging.Domain.Identity;
using EvCharging.Domain.Reservations;
using Xunit;

namespace EvCharging.Domain.Tests.Charging;

public sealed class OperationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

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

    private static Reservation Booking() => Reservation.Book(
        new Connector { Id = Guid.NewGuid(), ConnectorType = "Type2", PowerSupply = "AC" },
        Driver.Register("https://test.invalid", "driver", EmailAddress.Create("driver@example.test").Value),
        Start,
        Start.AddHours(1),
        Start,
        new(2, TimeSpan.FromHours(2), TimeSpan.FromDays(1), TimeSpan.FromMinutes(10))).Value;
}
