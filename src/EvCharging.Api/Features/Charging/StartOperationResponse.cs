using EvCharging.Domain.Charging;

namespace EvCharging.Api.Features.Charging;

public sealed record StartOperationResponse(
    Guid Id,
    Guid SessionId,
    CommandOutcome Outcome,
    SessionLifecycle Lifecycle,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? StartedAt);

internal static class StartOperationMappings
{
    public static StartOperationResponse ToResponse(this Operation operation)
    {
        var session = operation.Session ?? throw new InvalidOperationException("Session must be loaded.");
        return new(
            operation.PublicId,
            session.PublicId,
            operation.Outcome,
            session.Lifecycle,
            operation.AcceptedAt,
            session.StartedAt);
    }
}
