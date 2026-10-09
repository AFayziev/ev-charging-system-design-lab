namespace EvCharging.Api.Features.Stations;

internal static class StationBrowsingExtensions
{
    public static IHostApplicationBuilder AddStationBrowsing(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<ListStationsQueryHandler>();
        builder.Services.AddScoped<GetStationQueryHandler>();
        return builder;
    }
}
