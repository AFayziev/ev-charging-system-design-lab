namespace EvCharging.Gateway.Diagnostics;

internal sealed record FrontendDiagnosticsConfigResponse(
    bool Enabled,
    string? Dsn,
    string? Environment,
    string? Release,
    double ErrorSampleRate,
    double TraceSampleRate,
    FrontendLogLevel[] LogLevels,
    bool ReplayEnabled);

