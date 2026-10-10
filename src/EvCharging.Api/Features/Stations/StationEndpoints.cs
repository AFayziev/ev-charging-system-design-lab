using EvCharging.Api.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.Api.Features.Stations;

internal static class StationEndpoints
{
    public static WebApplication MapStationBrowsing(this WebApplication app)
    {
        var stations = app.MapGroup("/stations");
        stations.MapGet("/", List).AllowAnonymous();
        stations.MapGet("/{id:guid}", Detail).AllowAnonymous();
        return app;
    }

    private static async Task<Results<Ok<StationListResponse>, ValidationProblem>> List(
        [AsParameters] ListStationsRequest request,
        ListStationsQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.HasError<ValidationError>(out var errors)
            ? errors.ToResponse() : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<StationDetailResponse>, NotFound>> Detail(
        [AsParameters] GetStationRequest request,
        GetStationQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.HasError<NotFoundError<Guid>>(out var errors)
            ? errors.First().ToResponse() : TypedResults.Ok(result.Value);
    }
}
