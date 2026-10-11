using System.Security.Claims;
using EvCharging.Api.Errors;
using EvCharging.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

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

    private static async Task<Results<Ok<AssignedStationListResponse>, ValidationProblem, ForbidHttpResult>> Assigned(
        [AsParameters] ListStationsRequest request,
        ClaimsPrincipal user,
        ListAssignedStationsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, user, cancellationToken);
        return result switch
        {
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<StationEditorResponse>, NotFound, ForbidHttpResult>> Editor(
        Guid id, ClaimsPrincipal user, GetStationEditorQueryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, user, cancellationToken);
        return result switch
        {
            _ when result.HasError<NotFoundError<Guid>>(out var errors) => errors.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<StationEditorResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> Update(
        Guid id,
        UpdateStationRequest request,
        ClaimsPrincipal user,
        UpdateStationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, user, cancellationToken);
        return result switch
        {
            _ when result.HasError<NotFoundError<Guid>>(out var errors) => errors.First().ToResponse(),
            _ when result.HasError<ForbiddenError>() => TypedResults.Forbid(),
            _ when result.HasError<ConcurrencyConflictError>(out var conflicts) => conflicts.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<StationListResponse>, ValidationProblem>> List(
        [AsParameters] ListStationsRequest request,
        ListStationsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result switch
        {
            _ when result.HasError<ValidationError>(out var errors) => errors.ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }

    private static async Task<Results<Ok<StationDetailResponse>, NotFound>> Detail(
        [AsParameters] GetStationRequest request,
        GetStationQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result switch
        {
            _ when result.HasError<NotFoundError<Guid>>(out var errors) => errors.First().ToResponse(),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
