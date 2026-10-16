using EvCharging.Persistence.Entities;

namespace EvCharging.ChargingWorker.Providers;

internal sealed record ProviderEquipmentAddress(string StationId, string ChargerId, string ConnectorId);

internal static class ProviderEquipmentMappingExtensions
{
    public static ProviderEquipmentAddress ToProviderAddress(
        this ProviderConnector connector, ProviderCharger charger, ProviderStation station)
    {
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
