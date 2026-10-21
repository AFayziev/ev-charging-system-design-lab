using System.ComponentModel.DataAnnotations;

namespace EvCharging.Api.Streaming;

internal sealed class ObservationStreamOptions
{
    public const string SectionName = "ObservationStream";

    [Required]
    public string Topic { get; set; } = "";
}
