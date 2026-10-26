using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.SearchApi.Features.Stations;

internal static class StationSearchEndpoints
{
    public const string UnavailableCode = "search-unavailable";

    public static IServiceCollection AddStationSearch(this IServiceCollection services) =>
        services.AddValidation()
            .AddStationSearchResilience()
            .AddSingleton<StationSearchService>();

    public static WebApplication MapStationSearch(this WebApplication app)
    {
        app.MapGet("/search/stations", Search);
        app.MapGet("/search/stations/options", GetOptions);
        return app;
    }

    private static async Task<Results<Ok<StationSearchResponse>, ValidationProblem, ProblemHttpResult>> Search(
        [AsParameters] SearchStationsRequest request,
        StationSearchService searcher,
        CancellationToken cancellationToken)
    {
        var page = request.Cursor is null
            ? new StationSearchPage(request, [])
            : StationSearchCursor.Decode(request.Cursor, request.Limit);
        if (page is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Cursor)] = ["The cursor is not valid."]
            });
        }

        try
        {
            return TypedResults.Ok(await searcher.SearchAsync(page, cancellationToken));
        }
        catch (SearchUnavailableException)
        {
            return Unavailable();
        }
    }

    private static async Task<Results<Ok<StationSearchOptionsResponse>, ProblemHttpResult>> GetOptions(
        StationSearchService searcher,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await searcher.GetOptionsAsync(cancellationToken));
        }
        catch (SearchUnavailableException)
        {
            return Unavailable();
        }
    }

    private static ProblemHttpResult Unavailable() => TypedResults.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Search unavailable",
        detail: "Station search is temporarily unavailable.",
        extensions: new Dictionary<string, object?> { ["code"] = UnavailableCode });
}
