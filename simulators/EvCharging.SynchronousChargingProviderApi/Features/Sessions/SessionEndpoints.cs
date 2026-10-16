using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.SynchronousChargingProviderApi.Features.Sessions;

internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/sessions");
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/by-reference/{reference:guid}", GetByReferenceAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<SessionResponse>, NotFound>> GetAsync(
        Guid id, GetSessionQueryHandler handler, CancellationToken cancellationToken)
    {
        var session = await handler.HandleAsync(id, cancellationToken);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(session);
    }

    private static async Task<Results<Ok<SessionResponse>, NotFound>> GetByReferenceAsync(
        Guid reference, GetSessionByReferenceQueryHandler handler, CancellationToken cancellationToken)
    {
        var session = await handler.HandleAsync(reference, cancellationToken);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(session);
    }
}
