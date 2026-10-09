using System.ComponentModel.DataAnnotations;

namespace EvCharging.Api.Authentication;

internal sealed class ApiAuthenticationOptions
{
    [Required, Url]
    public string Authority { get; set; } = "";

    [Required, Url]
    public string MetadataAddress { get; set; } = "";

    [Required]
    public string Audience { get; set; } = "";

    [Required]
    public bool? AllowLocalHttp { get; set; }
}
