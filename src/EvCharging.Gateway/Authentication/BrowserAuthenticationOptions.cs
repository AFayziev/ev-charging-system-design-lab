using System.ComponentModel.DataAnnotations;

namespace EvCharging.Gateway.Authentication;

internal sealed class BrowserAuthenticationOptions
{
    [Required]
    public string PublicOrigin { get; set; } = "";

    [Required]
    public string SignInErrorPath { get; set; } = "";

    [Required]
    public string Authority { get; set; } = "";

    [Required]
    public string MetadataAddress { get; set; } = "";

    [Required]
    public string ClientId { get; set; } = "";

    [Required]
    public string ClientSecret { get; set; } = "";

    [Required]
    public bool? AllowLocalHttp { get; set; }
}
