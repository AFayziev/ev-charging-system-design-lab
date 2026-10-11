namespace EvCharging.Persistence.Entities;

public sealed class OperatorAssignment
{
    public Guid OperatorUserId { get; set; }
    public Guid StationId { get; set; }
}
