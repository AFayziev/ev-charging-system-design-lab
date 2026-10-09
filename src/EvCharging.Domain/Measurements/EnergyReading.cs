using EvCharging.Domain.Equipment;

namespace EvCharging.Domain.Measurements;

public sealed record EnergyReading
{
    public EnergyReading(
        ProviderIdentity providerSession,
        decimal energyKWh,
        EnergyMeaning meaning,
        DateTimeOffset occurredAt,
        bool isFinal)
    {
        ProviderSession = providerSession;
        EnergyKWh = energyKWh;
        Meaning = meaning;
        OccurredAt = occurredAt.ToUniversalTime();
        IsFinal = isFinal;
    }

    public ProviderIdentity ProviderSession { get; }
    public decimal EnergyKWh { get; }
    public EnergyMeaning Meaning { get; }
    public DateTimeOffset OccurredAt { get; }
    public bool IsFinal { get; }
}
