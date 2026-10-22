using EvCharging.Api.Errors;
using EvCharging.Contracts;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine;

namespace EvCharging.Api.Features.Charging;

internal static class ChargingSessionEndpoints
{
    public static WebApplication MapChargingSessions(this WebApplication app)
    {
        var routes = app.MapGroup("/charging-sessions/{sessionId:guid}")
            .RequireAuthorization(PlatformRole.Driver.ToClaimValue());
        routes.MapGet("/progress", Progress);
        routes.MapGet("/power", Power);
        return app;
    }

    private static async Task<Results<Ok<ChargingProgressResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult>>
        Progress(Guid sessionId, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<ChargingProgressResponse>>(
            new GetChargingProgressRequest(sessionId), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<ChargingPowerResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult>>
        Power(Guid sessionId, DateTimeOffset? from, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<ChargingPowerResponse>>(
            new GetChargingPowerRequest(sessionId, from), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
