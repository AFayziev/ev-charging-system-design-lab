using EvCharging.Domain.Charging;

namespace EvCharging.Persistence.Entities;

public sealed class Operation
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public long DriverId { get; set; }
    public ChargingCommandKind Kind { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public CommandOutcome Outcome { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}
