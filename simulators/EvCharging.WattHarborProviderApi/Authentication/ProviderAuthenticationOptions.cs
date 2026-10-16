using System.ComponentModel.DataAnnotations;

namespace EvCharging.WattHarborProviderApi.Authentication;

internal sealed class ProviderAuthenticationOptions
{
    [Required, Url]
    public string Authority { get; set; } = "";

    [Required]
    public string Audience { get; set; } = "";

    public bool AllowLocalHttp { get; set; }
}
