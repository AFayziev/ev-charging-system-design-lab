using System.ComponentModel.DataAnnotations;

namespace EvCharging.Gateway.Diagnostics;

internal sealed class FrontendDiagnosticsOptions
{
    public bool Enabled { get; set; }
    public string? Dsn { get; set; }
    public string? Environment { get; set; }
    public string? Release { get; set; }
    [Range(0, 1)]
    public double ErrorSampleRate { get; set; } = 1;
    [Range(0, 1)]
    public double TraceSampleRate { get; set; }
    public FrontendLogLevel[] LogLevels { get; set; } =
        [FrontendLogLevel.Warn, FrontendLogLevel.Error, FrontendLogLevel.Fatal];
    public bool ReplayEnabled { get; set; }
}
