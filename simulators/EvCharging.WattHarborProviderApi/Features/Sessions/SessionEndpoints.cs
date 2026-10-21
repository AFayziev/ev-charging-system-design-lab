using EvCharging.WattHarborProviderApi.Features.Telemetry;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.WattHarborProviderApi.Features.Sessions;

internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/sessions");
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/by-external-session/{externalSessionId:guid}", GetByExternalSessionIdAsync);
        group.MapGet("/by-external-session/{externalSessionId:guid}/snapshot", GetSnapshotAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<ObservationResponse>, NotFound>> GetSnapshotAsync(
        Guid externalSessionId, GetSessionSnapshotQueryHandler handler, CancellationToken cancellationToken)
    {
        var snapshot = await handler.HandleAsync(externalSessionId, cancellationToken);
        return snapshot is null ? TypedResults.NotFound() : TypedResults.Ok(snapshot);
    }

    private static async Task<Results<Ok<SessionResponse>, NotFound>> GetAsync(
        Guid id, GetSessionQueryHandler handler, CancellationToken cancellationToken)
    {
        var session = await handler.HandleAsync(id, cancellationToken);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(session);
    }

    private static async Task<Results<Ok<SessionResponse>, NotFound>> GetByExternalSessionIdAsync(
        Guid externalSessionId, GetSessionByExternalSessionIdQueryHandler handler, CancellationToken cancellationToken)
    {
        var session = await handler.HandleAsync(externalSessionId, cancellationToken);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(session);
    }
}
