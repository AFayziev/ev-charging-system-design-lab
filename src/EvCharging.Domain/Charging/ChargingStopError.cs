using FluentResults;

namespace EvCharging.Domain.Charging;

public sealed class ChargingStopError(string code, string message) : Error(message)
{
    public const string SessionNotActiveCode = "session_not_active";
    public const string StopInProgressCode = "stop_in_progress";
    public const string OperationResolvedCode = "stop_operation_resolved";
    public const string SessionNotStoppingCode = "session_not_stopping";

    public string Code { get; } = code;
}
