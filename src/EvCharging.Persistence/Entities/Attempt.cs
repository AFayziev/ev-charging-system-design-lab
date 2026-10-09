using EvCharging.Domain.Charging;

namespace EvCharging.Persistence.Entities;

public sealed class Attempt
{
    public long Id { get; set; }
    public long OperationId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset AuthorizedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public CommandOutcome? Outcome { get; set; }
    public string? FailureCode { get; set; }
}
