using System.ComponentModel.DataAnnotations;

namespace EvCharging.ChargingWorker.Configuration;

public sealed class ChargingExecutionOptions
{
    [Range(1, 10)]
    public int MaximumEffectAttempts { get; set; }
    [Range(1, 32)]
    public int MaximumConcurrentStarts { get; set; }
}
