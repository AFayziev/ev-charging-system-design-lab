using System.ComponentModel.DataAnnotations;

namespace EvCharging.Gateway.Persistence;

public sealed class AuthenticationDatabaseOptions
{
    [Range(1, 32)]
    public int MaximumPoolSize { get; set; } = 5;
    [Range(1, 30)]
    public int AcquisitionTimeoutSeconds { get; set; } = 2;
    [Range(1, 30)]
    public int CommandTimeoutSeconds { get; set; } = 3;
}
