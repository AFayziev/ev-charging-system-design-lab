using System.ComponentModel.DataAnnotations;

namespace EvCharging.ChargingWorker.Configuration;

public sealed class ChargingExecutionOptions
{
    public const string SectionName = "ChargingExecution";

    [Range(1, 10)]
    public int MaximumCommandRequests { get; set; }
    [Range(1, 32)]
    public int MaximumConcurrentCommands { get; set; }
    [Range(1, 12)]
    public int MaximumAutomaticStatusChecks { get; set; }
    public TimeSpan AutomaticStatusCheckInterval { get; set; }

    /// <summary>Minimum gap between continued checks of an unresolved operation.</summary>
    public TimeSpan ContinuedStatusCheckInterval { get; set; }

    /// <summary>Maximum gap between continued checks.</summary>
    public TimeSpan MaximumContinuedStatusCheckInterval { get; set; }

    /// <summary>Cron schedule of the continued-check scan.</summary>
    public string ContinuedStatusCheckSchedule { get; set; } = "";
}
