using System.ComponentModel.DataAnnotations;

namespace EvCharging.Api.Configuration;

public sealed class ChargingStatusCheckOptions
{
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan StatusCheckTimeout { get; set; }
}
