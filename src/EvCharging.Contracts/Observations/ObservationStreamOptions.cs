using System.ComponentModel.DataAnnotations;

namespace EvCharging.Contracts.Observations;

/// <summary>Where accepted observation envelopes are appended and consumed.</summary>
public sealed class ObservationStreamOptions
{
    public const string SectionName = "ObservationStream";

    [Required]
    public string Topic { get; set; } = "";
}
