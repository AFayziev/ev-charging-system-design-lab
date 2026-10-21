using EvCharging.Domain.Charging;

namespace EvCharging.Persistence.Entities;

public sealed class Attempt
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public Operation? Operation { get; set; }
    public int Number { get; set; }
    public DateTimeOffset AuthorizedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public CommandOutcome? Outcome { get; set; }
    public string? FailureCode { get; set; }
    public Guid ChargingProviderId { get; set; }
    public string IntegrationKey { get; set; } = "";
    public string ProviderStationId { get; set; } = "";
    public string ProviderChargerId { get; set; } = "";
    public string ProviderConnectorId { get; set; } = "";
    public Guid OperationPublicId { get; set; }
    public Guid SessionPublicId { get; set; }
    public Guid? ProviderCommandId { get; set; }
    public Guid? ProviderSessionId { get; set; }
    public DateTimeOffset? ProviderAppliedAt { get; set; }
    public string? ResponseBody { get; set; }
    public int? ResponseStatusCode { get; set; }
}
