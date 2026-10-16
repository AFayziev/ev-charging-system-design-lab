using EvCharging.Domain.Auditing;
using EvCharging.Domain.Reservations;
using FluentResults;

namespace EvCharging.Domain.Charging;

public sealed class ChargingSession : IAuditableEntity
{
    private ChargingSession()
    {
    }

    public Guid Id { get; private set; }
    public Guid PublicId { get; private set; }
    public Guid ReservationId { get; private set; }
    public Reservation? Reservation { get; private set; }
    public Guid ConnectorId { get; private set; }
    public SessionLifecycle Lifecycle { get; private set; }
    public Guid? ChargingProviderId { get; private set; }
    public string? ProviderSessionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public decimal? FinalEnergyKWh { get; private set; }

    public static Result<ChargingSession> AcceptStart(Reservation reservation, DateTimeOffset now)
    {
        var eligibility = reservation.ValidateChargingStart(now);
        if (eligibility.IsFailed)
        {
            return Result.Fail<ChargingSession>(eligibility.Errors);
        }

        return Result.Ok(new ChargingSession
        {
            ReservationId = reservation.Id,
            Reservation = reservation,
            ConnectorId = reservation.ConnectorId,
            Lifecycle = SessionLifecycle.Starting,
            CreatedAt = now.ToUniversalTime()
        });
    }

    public Result ConfirmStart(Guid providerId, string providerSessionId, DateTimeOffset startedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(providerId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);
        if (Lifecycle != SessionLifecycle.Starting)
        {
            return Result.Fail(new ChargingStartError(
                ChargingStartError.SessionNotStartingCode, "Only a starting session can be confirmed."));
        }

        var reservation = Reservation ?? throw new InvalidOperationException("Reservation must be loaded.");
        var consumption = reservation.Consume();
        if (consumption.IsFailed)
        {
            return consumption;
        }

        ChargingProviderId = providerId;
        ProviderSessionId = providerSessionId;
        StartedAt = startedAt.ToUniversalTime();
        Lifecycle = SessionLifecycle.Active;
        return Result.Ok();
    }

    public Result RejectStart()
    {
        if (Lifecycle != SessionLifecycle.Starting)
        {
            return Result.Fail(new ChargingStartError(
                ChargingStartError.SessionNotStartingCode, "Only a starting session can be rejected."));
        }

        Lifecycle = SessionLifecycle.Failed;
        return Result.Ok();
    }
}
