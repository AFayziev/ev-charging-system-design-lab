using EvCharging.Persistence.Entities;

namespace EvCharging.ChargingWorker.Providers;

public sealed record ProviderEquipmentAddress(string StationId, string ChargerId, string ConnectorId);

internal static class ProviderEquipmentMappingExtensions
{
    public static ProviderEquipmentAddress ToProviderAddress(this ProviderConnector connector)
    {
        var charger = connector.ProviderCharger
            ?? throw new InvalidOperationException("The provider charger must be loaded.");
        var station = charger.ProviderStation
            ?? throw new InvalidOperationException("The provider station must be loaded.");
        if (connector.ProviderChargerId != charger.Id || charger.ProviderStationId != station.Id)
        {
            throw new ArgumentException("The provider equipment mappings must belong to the same address.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(station.ExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(charger.ExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connector.ExternalId);
        return new ProviderEquipmentAddress(station.ExternalId, charger.ExternalId, connector.ExternalId);
    }
}
