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

    /// <summary>When an operator released the connector held by this uncertain start.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>Settled operations and released starts accept no further evidence.</summary>
    public bool IsSettledOrReleased =>
        Outcome is CommandOutcome.Confirmed or CommandOutcome.Rejected || ReleasedAt is not null;

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

    public Result ConfirmStart(Guid providerId, string providerSessionId, DateTimeOffset startedAt) =>
        ConfirmStartWith(session => session.ConfirmStart(providerId, providerSessionId, startedAt));

    /// <summary>Confirms the start from observations, which do not identify the provider's session.</summary>
    public Result ConfirmObservedStart(Guid providerId, DateTimeOffset startedAt) =>
        ConfirmStartWith(session => session.ConfirmObservedStart(providerId, startedAt));

    private Result ConfirmStartWith(Func<ChargingSession, Result> confirmSession)
    {
        var eligibility = ValidateUnresolved(ChargingCommandKind.Start);
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        var confirmation = confirmSession(Session ?? throw new InvalidOperationException("Session must be loaded."));
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

    /// <summary>
    /// Releases the connector held by an uncertain start that no evidence has settled. The session fails, and the
    /// outcome stays uncertain. Clearing the checking window ends automatic checks: the continued-check scan and
    /// scheduled checks only run while it is set. Releasing an already released start changes nothing.
    /// </summary>
    public Result ReleaseStart(
        DateTimeOffset now, DateTimeOffset lastRequestedAt, TimeSpan releaseWindow, bool hasObservations)
    {
        if (ReleasedAt is not null)
        {
            return Result.Ok();
        }

        var eligibility = ValidateStartRelease(now, lastRequestedAt, releaseWindow, hasObservations);
        if (eligibility.IsFailed)
        {
            return eligibility;
        }

        var release = (Session ?? throw new InvalidOperationException("Session must be loaded.")).ReleaseStart();
        if (release.IsFailed)
        {
            return release;
        }

        ReleasedAt = now.ToUniversalTime();
        AutomaticChecksEndAt = null;
        return Result.Ok();
    }

    /// <summary>When a start's release window after its last request ends; stops cannot be released.</summary>
    public DateTimeOffset? StartReleasableAt(DateTimeOffset lastRequestedAt, TimeSpan releaseWindow) =>
        Kind == ChargingCommandKind.Start ? lastRequestedAt + releaseWindow : null;

    /// <summary>
    /// An uncertain start can be released once the release window after its last request has passed with no
    /// observations from the provider.
    /// </summary>
    public Result ValidateStartRelease(
        DateTimeOffset now, DateTimeOffset lastRequestedAt, TimeSpan releaseWindow, bool hasObservations)
    {
        if (Kind != ChargingCommandKind.Start || Outcome != CommandOutcome.Uncertain || ReleasedAt is not null)
        {
            return NotReleasable("Only an uncertain start that has not been released can be released.");
        }

        if (hasObservations)
        {
            return NotReleasable("The provider has sent observations for this session, so the start is not released.");
        }

        return now < lastRequestedAt + releaseWindow
            ? NotReleasable("The start can be released once the release window after its last request ends.")
            : Result.Ok();
    }

    private static Result NotReleasable(string message) =>
        Result.Fail(new ChargingStartError(ChargingStartError.StartNotReleasableCode, message));

    private Result ValidateUnresolved(ChargingCommandKind expected)
    {
        if (Kind == expected && ReleasedAt is null && Outcome is (CommandOutcome.Pending or CommandOutcome.Uncertain))
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
