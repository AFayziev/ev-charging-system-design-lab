using System.ComponentModel.DataAnnotations;

namespace EvCharging.Gateway.Authentication;

public sealed class SessionStoreOptions
{
    [Range(1, 120)]
    public int RefreshLeaseSeconds { get; set; } = 30;
}
