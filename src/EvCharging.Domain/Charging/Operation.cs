using EvCharging.Domain.Auditing;

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
}
