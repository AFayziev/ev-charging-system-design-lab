using EvCharging.Domain.Equipment;

namespace EvCharging.Domain.Measurements;

public static class UsagePolicy
{
    public static bool IsValidTotal(
        ProviderIdentity session,
        EnergyReading reading,
        EnergyReading? latest) =>
        reading.ProviderSession == session
        && reading.Meaning == EnergyMeaning.SessionCumulative
        && reading.EnergyKWh >= 0
        && (latest is null
            || (latest.ProviderSession == session
                && latest.Meaning == EnergyMeaning.SessionCumulative
                && latest.EnergyKWh >= 0
                && reading.EnergyKWh >= latest.EnergyKWh));

    public static EnergyReading? AcceptTotal(
        ProviderIdentity session,
        EnergyReading? latest,
        EnergyReading candidate) =>
        IsValidTotal(session, candidate, latest)
            && (latest is null || candidate.OccurredAt >= latest.OccurredAt)
            ? candidate
            : latest;
}
