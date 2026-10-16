using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EvCharging.SynchronousChargingProviderApi.Features.Commands;

internal static class CommandEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/commands");
        group.MapPost("/start", StartAsync)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/by-reference/{reference:guid}", GetByReferenceAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult, ValidationProblem>> StartAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] Guid idempotencyKey,
        StartChargingRequest request,
        StartChargingCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (idempotencyKey == Guid.Empty)
        {
            errors[IdempotencyKeyHeader] = ["A non-empty UUID idempotency key is required."];
        }

        if (request.SessionReference == Guid.Empty)
        {
            errors[nameof(request.SessionReference)] = ["A non-empty session reference is required."];
        }

        if (errors.Count != 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await handler.HandleAsync(idempotencyKey, request, cancellationToken);
        return result switch
        {
            _ when result.HasError<IdempotencyConflictError>(out var conflicts) => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency key conflict",
                detail: conflicts.First().Message,
                extensions: new Dictionary<string, object?> { ["code"] = ProviderErrorCodes.IdempotencyConflict }),
            _ when result.HasError<ChargingStartRejectedError>(out var rejections) => ToProblem(rejections.First()),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static ProblemHttpResult ToProblem(ChargingStartRejectedError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Charging start rejected",
        detail: error.Message,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["commandId"] = error.CommandId,
            ["reference"] = error.Reference
        });

    private static async Task<Results<Ok<CommandResponse>, NotFound>> GetAsync(
        Guid id, GetCommandQueryHandler handler, CancellationToken cancellationToken)
    {
        var command = await handler.HandleAsync(id, cancellationToken);
        return command is null ? TypedResults.NotFound() : TypedResults.Ok(command);
    }

    private static async Task<Results<Ok<CommandResponse>, NotFound>> GetByReferenceAsync(
        Guid reference, GetCommandByReferenceQueryHandler handler, CancellationToken cancellationToken)
    {
        var command = await handler.HandleAsync(reference, cancellationToken);
        return command is null ? TypedResults.NotFound() : TypedResults.Ok(command);
    }
}
