namespace EvCharging.SynchronousChargingProviderApi.Persistence;

internal sealed class ProviderConnector
{
    public string StationId { get; init; } = "";
    public string ChargerId { get; init; } = "";
    public string ConnectorId { get; init; } = "";
    public decimal PowerKw { get; set; }
    public bool Enabled { get; set; }
    public Guid? ActiveCommandId { get; private set; }
}
