namespace EvCharging.SynchronousChargingProviderApi.Persistence;

internal sealed class ProviderSession
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid PublicId { get; private set; } = Guid.NewGuid();
    public Guid Reference { get; init; }
    public string StationId { get; init; } = "";
    public string ChargerId { get; init; } = "";
    public string ConnectorId { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public decimal PowerKw { get; init; }
}
