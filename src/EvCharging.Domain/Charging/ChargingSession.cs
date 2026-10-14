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
}
