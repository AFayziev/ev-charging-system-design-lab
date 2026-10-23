using EvCharging.ChargingWorker.Providers;

namespace EvCharging.ChargingWorker.Handlers;

/// <summary>Where a command's requests go: the provider, its integration, and the saved equipment address.</summary>
internal sealed record ProviderBinding(Guid ProviderId, string IntegrationKey, ProviderEquipmentAddress Address);
