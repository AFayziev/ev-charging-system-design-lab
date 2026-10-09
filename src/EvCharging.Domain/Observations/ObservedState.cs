namespace EvCharging.Domain.Observations;

public enum ObservedState
{
    Available,
    Charging,
    Stopped,
    Faulted,
    Offline,
    Unknown
}
