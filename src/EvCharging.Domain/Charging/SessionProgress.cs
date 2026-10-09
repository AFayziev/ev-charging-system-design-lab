using EvCharging.Domain.Equipment;
using EvCharging.Domain.Measurements;

namespace EvCharging.Domain.Charging;

public sealed record SessionProgress(
    SessionLifecycle Lifecycle,
    ProviderIdentity? ProviderSession,
    EnergyReading? FinalUsage = null);
