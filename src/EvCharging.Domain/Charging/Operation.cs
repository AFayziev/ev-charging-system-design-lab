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
    public int? StartRequestLimit { get; private set; }
    public DateTimeOffset? AutomaticChecksEndAt { get; private set; }

    public Result ScheduleAutomaticChecks(DateTimeOffset endsAt)
    {
        if (Kind != ChargingCommandKind.Start || Outcome != CommandOutcome.Pending
            || AuthorizedAttemptCount != 1 || AutomaticChecksEndAt is not null || endsAt <= AcceptedAt)
        {
            return Result.Fail("Automatic checks require a newly authorized start and a cutoff after acceptance.");
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

    public Result ConfirmStart(Guid providerId, string providerSessionId, DateTimeOffset startedAt)
    {
        var eligibility = ValidateUnresolvedStart();
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
        var eligibility = ValidateUnresolvedStart();
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

    public Result MarkUncertain()
    {
        var eligibility = ValidateUnresolvedStart();
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        Outcome = CommandOutcome.Uncertain;
        return Result.Ok();
    }

    private Result ValidateUnresolvedStart()
    {
        if (Kind != ChargingCommandKind.Start || Outcome is not (CommandOutcome.Pending or CommandOutcome.Uncertain))
        {
            return Result.Fail(new ChargingStartError(
                ChargingStartError.OperationResolvedCode, "Only an unresolved start can receive an outcome."));
        }

        return Result.Ok();
    }
}
