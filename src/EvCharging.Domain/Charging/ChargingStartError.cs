using FluentResults;

namespace EvCharging.Domain.Charging;

public sealed class ChargingStartError(string code, string message) : Error(message)
{
    public const string ReservationInactiveCode = "reservation_inactive";
    public const string TooEarlyCode = "start_too_early";
    public const string WindowEndedCode = "start_window_ended";

    public string Code { get; } = code;
}
