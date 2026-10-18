namespace EvCharging.Domain.Equipment;

public sealed class ChargingProvider
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? IntegrationKey { get; set; }
}
