using System.Net;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EvCharging.Api.Features.Charging;

public sealed record StartOperationResponse(
    Guid Id,
    Guid SessionId,
    CommandOutcome Outcome,
    SessionLifecycle Lifecycle,
    DateTimeOffset AcceptedAt,
    DateTimeOffset? StartedAt,
    StartFailureReason? FailureReason = null);

public enum StartFailureReason
{
    ChargingServiceUnavailable
}

internal static class StartOperationMappings
{
    public static StartOperationResponse ToResponse(
        this Operation operation, int? responseStatusCode = null)
    {
        var session = operation.Session ?? throw new InvalidOperationException("Session must be loaded.");
        return new(
            operation.PublicId,
            session.PublicId,
            operation.Outcome,
            session.Lifecycle,
            operation.AcceptedAt,
            session.StartedAt,
            operation.Outcome == CommandOutcome.Rejected
                && responseStatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden
                    ? StartFailureReason.ChargingServiceUnavailable : null);
    }

    public static async Task<StartOperationResponse> ToResponseAsync(
        this Operation operation, PlatformDbContext database, CancellationToken cancellationToken)
    {
        var status = await database.Attempts.Where(attempt => attempt.OperationId == operation.Id)
            .OrderByDescending(attempt => attempt.Number)
            .Select(attempt => attempt.ResponseStatusCode)
            .FirstOrDefaultAsync(cancellationToken);
        return operation.ToResponse(status);
    }
}
