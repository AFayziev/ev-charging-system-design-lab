using EvCharging.Domain.Charging;

namespace EvCharging.Persistence.Entities;

public sealed class Operation
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid DriverId { get; set; }
    public ChargingCommandKind Kind { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public CommandOutcome Outcome { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}
