using System.ComponentModel.DataAnnotations;

namespace EvCharging.Api.Authentication;

/// <summary>Provider integrations allowed to push observations, keyed by integration key.</summary>
internal sealed class ObservationSourceOptions
{
    public const string SectionName = "ObservationSources";

    [Required, MinLength(1)]
    public Dictionary<string, ObservationSource> Sources { get; set; } = [];
}

internal sealed class ObservationSource
{
    /// <summary>The provider adapter that parses this source's payloads, for example WattHarbor.</summary>
    [Required]
    public string Client { get; set; } = "";

    /// <summary>Accepted keys; several allow rotation without downtime.</summary>
    [Required, MinLength(1)]
    public string[] ApiKeys { get; set; } = [];
}
