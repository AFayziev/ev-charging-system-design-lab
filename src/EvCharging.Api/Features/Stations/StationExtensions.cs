namespace EvCharging.Api.Features.Stations;

internal static class StationExtensions
{
    public static IHostApplicationBuilder AddStations(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<ListStationsQueryHandler>();
        builder.Services.AddScoped<GetStationQueryHandler>();
        builder.Services.AddScoped<GetStationEditorQueryHandler>();
        builder.Services.AddScoped<UpdateStationCommandHandler>();
        builder.Services.AddScoped<ListAssignedStationsQueryHandler>();
        builder.Services.AddScoped<
            Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
            StationAssignmentAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        return builder;
    }
}
