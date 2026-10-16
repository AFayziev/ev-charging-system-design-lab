using EvCharging.Api.Errors;
using EvCharging.Contracts;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine;

namespace EvCharging.Api.Features.Charging;

internal static class ChargingEndpoints
{
    public static WebApplication MapCharging(this WebApplication app)
    {
        var routes = app.MapGroup("/reservations/{id:guid}").RequireAuthorization(PlatformRole.Driver.ToClaimValue());
        routes.MapPost("/start", Start);
        routes.MapPost("/start/{operationId:guid}/check", Check);
        return app;
    }

    private static async Task<Results<
        Accepted<StartOperationResponse>,
        ValidationProblem,
        NotFound,
        ChallengeHttpResult,
        ForbidHttpResult,
        ProblemHttpResult>> Start(
            Guid id, StartChargingRequest request, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StartOperationResponse>>(
            new StartChargingCommand(id, request.IdempotencyKey), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ when result.HasError<ChargingStartError>(out var errors) => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Charging start conflict",
                detail: errors.First().Message,
                extensions: new Dictionary<string, object?> { ["code"] = errors.First().Code }),
            _ => TypedResults.Accepted($"/api/reservations/{id}", result.Value)
        };
    }

    private static async Task<Results<
        Ok<CheckChargingStartResponse>,
        NotFound,
        ChallengeHttpResult,
        ForbidHttpResult,
        ProblemHttpResult>> Check(
            Guid id, Guid operationId, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<CheckChargingStartResponse>>(
            new CheckChargingStartCommand(id, operationId), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<NotFoundError<Guid>>(out var missing) => missing.First().ToResponse(),
            _ when result.Value.Outcome == ChargingStartCheckOutcome.Unavailable =>
                TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Could not check the station",
                    detail: "We could not get a result from the station. Your start remains unresolved. "
                        + "Please try checking again."),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
