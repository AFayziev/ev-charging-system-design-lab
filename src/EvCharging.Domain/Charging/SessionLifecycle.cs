namespace EvCharging.Domain.Charging;

public enum SessionLifecycle
{
    Starting,
    Active,
    Stopping,
    Completed,
    Failed
}
