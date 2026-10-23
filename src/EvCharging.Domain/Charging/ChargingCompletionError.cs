using FluentResults;

namespace EvCharging.Domain.Charging;

public sealed class ChargingCompletionError(string code, string message) : Error(message)
{
    public const string SessionNotCompletableCode = "session_not_completable";

    public string Code { get; } = code;
}
