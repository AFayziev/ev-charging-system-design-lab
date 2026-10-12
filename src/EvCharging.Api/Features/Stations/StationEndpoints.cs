using EvCharging.Api.Errors;
using EvCharging.Contracts;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine;

namespace EvCharging.Api.Features.Stations;

internal static class StationEndpoints
{
    public static WebApplication MapStations(this WebApplication app)
    {
        var stations = app.MapGroup("/stations");
        stations.MapGet("/", List).AllowAnonymous();
        stations.MapGet("/{id:guid}", Detail).AllowAnonymous();
        stations.MapGet("/assigned", Assigned).RequireAuthorization(PlatformRole.Operator.ToClaimValue());
        stations.MapGet("/{id:guid}/edit", Editor).RequireAuthorization(PlatformRole.Operator.ToClaimValue());
        stations.MapPut("/{id:guid}", Update).RequireAuthorization(PlatformRole.Operator.ToClaimValue());
        return app;
    }

    private static async Task<Results<
        Ok<AssignedStationListResponse>, ValidationProblem, ChallengeHttpResult>> Assigned(
            [AsParameters] ListAssignedStationsRequest request,
            IMessageBus bus,
            CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<AssignedStationListResponse>>(request, cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<
        Ok<StationEditorResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult>> Editor(
            Guid id, IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationEditorResponse>>(
            new GetStationEditorRequest(id), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var errors) => errors.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<
        Ok<StationEditorResponse>, NotFound, ChallengeHttpResult, ForbidHttpResult, ProblemHttpResult>> Update(
            Guid id,
            UpdateStationRequest request,
            IMessageBus bus,
            CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationEditorResponse>>(
            new UpdateStationCommand(id, request), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<NotFoundError<Guid>>(out var errors) => errors.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ConcurrencyConflictError>(out var conflicts) => conflicts.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<StationListResponse>, ValidationProblem>> List(
        [AsParameters] ListStationsRequest request,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationListResponse>>(request, cancellationToken);
        return result switch
        {
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<StationDetailResponse>, NotFound>> Detail(
        [AsParameters] GetStationRequest request,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<StationDetailResponse>>(request, cancellationToken);
        return result switch
        {
            _ when result.HasError<NotFoundError<Guid>>(out var errors) => errors.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
