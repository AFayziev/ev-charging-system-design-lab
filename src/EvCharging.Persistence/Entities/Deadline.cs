namespace EvCharging.Persistence.Entities;

public sealed class Deadline
{
    public long Id { get; set; }
    public DeadlineKind Kind { get; set; }
    public long? ReservationId { get; set; }
    public long? OperationId { get; set; }
    public long? SessionId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
}
