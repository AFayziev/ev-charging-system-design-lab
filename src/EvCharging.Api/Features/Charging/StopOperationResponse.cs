using EvCharging.Domain.Charging;

namespace EvCharging.Api.Features.Charging;

public sealed record StopOperationResponse(
    Guid Id,
    Guid SessionId,
    CommandOutcome Outcome,
    DateTimeOffset AcceptedAt);

internal static class StopOperationMappings
{
    public static StopOperationResponse ToStopResponse(this Operation operation)
    {
        var session = operation.Session ?? throw new InvalidOperationException("Session must be loaded.");
        return new(operation.PublicId, session.PublicId, operation.Outcome, operation.AcceptedAt);
    }
}
