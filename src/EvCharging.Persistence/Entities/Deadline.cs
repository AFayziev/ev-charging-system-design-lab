namespace EvCharging.Persistence.Entities;

public sealed class Deadline
{
    public Guid Id { get; set; }
    public DeadlineKind Kind { get; set; }
    public Guid? ReservationId { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? SessionId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
}
