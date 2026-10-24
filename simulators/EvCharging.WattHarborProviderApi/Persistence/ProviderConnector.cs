namespace EvCharging.WattHarborProviderApi.Persistence;

internal sealed class ProviderConnector
{
    public string StationId { get; init; } = "";
    public string ChargerId { get; init; } = "";
    public string ConnectorId { get; init; } = "";
    public decimal PowerKw { get; set; }
    public bool Enabled { get; set; }

    /// <summary>The energy a vehicle on this connector takes before it is full; null means it never is.</summary>
    public decimal? EnergyLimitKWh { get; set; }
    public Guid? ActiveCommandId { get; private set; }
}
