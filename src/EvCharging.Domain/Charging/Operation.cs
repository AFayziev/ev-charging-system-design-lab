using EvCharging.Domain.Auditing;
using FluentResults;

namespace EvCharging.Domain.Charging;

public sealed class Operation : IAuditableEntity
{
    private Operation()
    {
    }

    public Guid Id { get; private set; }
    public Guid PublicId { get; private set; }
    public Guid SessionId { get; private set; }
    public ChargingSession? Session { get; private set; }
    public Guid DriverId { get; private set; }
    public ChargingCommandKind Kind { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public CommandOutcome Outcome { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }
    public int AuthorizedAttemptCount { get; private set; }
    public int? RequestLimit { get; private set; }
    public DateTimeOffset? AutomaticChecksEndAt { get; private set; }

    public Result ScheduleAutomaticChecks(DateTimeOffset endsAt)
    {
        if (Outcome != CommandOutcome.Pending || AuthorizedAttemptCount != 1
            || AutomaticChecksEndAt is not null || endsAt <= AcceptedAt)
        {
            return Result.Fail("Automatic checks require a newly authorized command and a cutoff after acceptance.");
        }

        AutomaticChecksEndAt = endsAt;
        return Result.Ok();
    }

    public static Operation AcceptStart(ChargingSession session, Guid key)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(key, Guid.Empty);
        var reservation = session.Reservation ?? throw new InvalidOperationException("Reservation must be loaded.");
        return new Operation
        {
            Session = session,
            SessionId = session.Id,
            DriverId = reservation.DriverId,
            Kind = ChargingCommandKind.Start,
            IdempotencyKey = key,
            Outcome = CommandOutcome.Pending,
            AcceptedAt = session.CreatedAt
        };
    }

    public static Result<Operation> AcceptStop(ChargingSession session, Guid key, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(key, Guid.Empty);
        var reservation = session.Reservation ?? throw new InvalidOperationException("Reservation must be loaded.");
        var acceptance = session.AcceptStop();
        if (acceptance.IsFailed)
        {
            return Result.Fail<Operation>(acceptance.Errors);
        }

        return Result.Ok(new Operation
        {
            Session = session,
            SessionId = session.Id,
            DriverId = reservation.DriverId,
            Kind = ChargingCommandKind.Stop,
            IdempotencyKey = key,
            Outcome = CommandOutcome.Pending,
            AcceptedAt = now.ToUniversalTime()
        });
    }

    public Result ConfirmStart(Guid providerId, string providerSessionId, DateTimeOffset startedAt)
    {
        var eligibility = ValidateUnresolved(ChargingCommandKind.Start);
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        var confirmation = (Session ?? throw new InvalidOperationException("Session must be loaded."))
            .ConfirmStart(providerId, providerSessionId, startedAt);
        if (confirmation.IsFailed)
        {
            return confirmation;
        }

        Outcome = CommandOutcome.Confirmed;
        return Result.Ok();
    }

    public Result RejectStart()
    {
        var eligibility = ValidateUnresolved(ChargingCommandKind.Start);
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        var rejection = (Session ?? throw new InvalidOperationException("Session must be loaded.")).RejectStart();
        if (rejection.IsFailed)
        {
            return rejection;
        }

        Outcome = CommandOutcome.Rejected;
        return Result.Ok();
    }

    /// <summary>
    /// Records that the stop took effect. The session stays stopping, because completion also needs final usage.
    /// </summary>
    public Result ConfirmStop()
    {
        var eligibility = ValidateUnresolved(ChargingCommandKind.Stop);
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        Outcome = CommandOutcome.Confirmed;
        return Result.Ok();
    }

    public Result RejectStop()
    {
        var eligibility = ValidateUnresolved(ChargingCommandKind.Stop);
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        var rejection = (Session ?? throw new InvalidOperationException("Session must be loaded.")).RejectStop();
        if (rejection.IsFailed)
        {
            return rejection;
        }

        Outcome = CommandOutcome.Rejected;
        return Result.Ok();
    }

    public Result MarkUncertain()
    {
        var eligibility = ValidateUnresolved(Kind);
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        Outcome = CommandOutcome.Uncertain;
        return Result.Ok();
    }

    private Result ValidateUnresolved(ChargingCommandKind expected)
    {
        if (Kind == expected && Outcome is (CommandOutcome.Pending or CommandOutcome.Uncertain))
        {
            return Result.Ok();
        }

        return expected == ChargingCommandKind.Start
            ? Result.Fail(new ChargingStartError(
                ChargingStartError.OperationResolvedCode, "Only an unresolved start can receive an outcome."))
            : Result.Fail(new ChargingStopError(
                ChargingStopError.OperationResolvedCode, "Only an unresolved stop can receive an outcome."));
    }
}
