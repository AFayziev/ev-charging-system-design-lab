namespace EvCharging.Contracts.Observations;

/// <summary>The physical state a provider reported.</summary>
public enum ObservationState
{
    Available,
    Charging,
    Stopped,
    Faulted,
    Offline,
    Unknown
}
