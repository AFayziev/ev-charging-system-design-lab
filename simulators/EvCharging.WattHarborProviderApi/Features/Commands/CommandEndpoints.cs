using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EvCharging.WattHarborProviderApi.Features.Commands;

internal static class CommandEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/commands");
        group.MapPost("/start", StartAsync)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/stop", StopAsync)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/by-external-operation/{externalOperationId:guid}", GetByExternalOperationIdAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult, ValidationProblem>> StartAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] Guid idempotencyKey,
        StartChargingRequest request,
        StartChargingCommandHandler handler,
        CancellationToken cancellationToken)
    {
        if (Validate(idempotencyKey, request.ExternalSessionId) is { } invalid)
        {
            return invalid;
        }

        var result = await handler.HandleAsync(idempotencyKey, request, cancellationToken);
        return result switch
        {
            _ when result.HasError<IdempotencyConflictError>(out var conflicts) => ToProblem(conflicts.First()),
            _ when result.HasError<ChargingStartRejectedError>(out var rejections) => ToProblem(rejections.First()),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult, ValidationProblem>> StopAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] Guid idempotencyKey,
        StopChargingRequest request,
        StopChargingCommandHandler handler,
        CancellationToken cancellationToken)
    {
        if (Validate(idempotencyKey, request.ExternalSessionId) is { } invalid)
        {
            return invalid;
        }

        var result = await handler.HandleAsync(idempotencyKey, request, cancellationToken);
        return result switch
        {
            _ when result.HasError<IdempotencyConflictError>(out var conflicts) => ToProblem(conflicts.First()),
            _ when result.HasError<ChargingStopRejectedError>(out var rejections) => ToProblem(rejections.First()),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static ValidationProblem? Validate(Guid idempotencyKey, Guid externalSessionId)
    {
        var errors = new Dictionary<string, string[]>();
        if (idempotencyKey == Guid.Empty)
        {
            errors[IdempotencyKeyHeader] = ["A non-empty UUID idempotency key is required."];
        }

        if (externalSessionId == Guid.Empty)
        {
            errors[nameof(StartChargingRequest.ExternalSessionId)] = ["A non-empty external session ID is required."];
        }

        return errors.Count == 0 ? null : (ValidationProblem)TypedResults.ValidationProblem(errors);
    }

    private static ProblemHttpResult ToProblem(IdempotencyConflictError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Idempotency key conflict",
        detail: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = ProviderErrorCodes.IdempotencyConflict });

    private static ProblemHttpResult ToProblem(ChargingStopRejectedError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Charging stop rejected",
        detail: error.Message,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["commandId"] = error.CommandId,
            ["externalOperationId"] = error.ExternalOperationId
        });

    private static ProblemHttpResult ToProblem(ChargingStartRejectedError error) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Charging start rejected",
        detail: error.Message,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["commandId"] = error.CommandId,
            ["externalOperationId"] = error.ExternalOperationId
        });

    private static async Task<Results<Ok<CommandResponse>, NotFound>> GetAsync(
        Guid id, GetCommandQueryHandler handler, CancellationToken cancellationToken)
    {
        var command = await handler.HandleAsync(id, cancellationToken);
        return command is null ? TypedResults.NotFound() : TypedResults.Ok(command);
    }

    private static async Task<Results<Ok<CommandResponse>, NotFound>> GetByExternalOperationIdAsync(
        Guid externalOperationId,
        GetCommandByExternalOperationIdQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var command = await handler.HandleAsync(externalOperationId, cancellationToken);
        return command is null ? TypedResults.NotFound() : TypedResults.Ok(command);
    }
}
