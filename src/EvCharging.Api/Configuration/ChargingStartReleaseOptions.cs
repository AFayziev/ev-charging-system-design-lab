using System.ComponentModel.DataAnnotations;

namespace EvCharging.Api.Configuration;

public sealed class ChargingStartReleaseOptions
{
    /// <summary>How long after its last request an uncertain start with no observations waits before it can be released.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan ReleaseWindow { get; set; }
}
