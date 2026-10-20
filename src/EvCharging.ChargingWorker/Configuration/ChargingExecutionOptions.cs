using System.ComponentModel.DataAnnotations;

namespace EvCharging.ChargingWorker.Configuration;

public sealed class ChargingExecutionOptions
{
    [Range(1, 10)]
    public int MaximumCommandRequests { get; set; }
    [Range(1, 32)]
    public int MaximumConcurrentCommands { get; set; }
    [Range(1, 12)]
    public int MaximumAutomaticStatusChecks { get; set; }
    public TimeSpan AutomaticStatusCheckInterval { get; set; }
}
