using EvCharging.Api.Features.Reservations;
using EvCharging.Api.Features.Stations;
using Microsoft.AspNetCore.Authorization;

namespace EvCharging.Api.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddOptions<ReservationPolicyOptions>()
            .BindConfiguration("ReservationPolicy")
            .Validate(
                options => options.MaximumOutstandingReservationsPerDriver > 0,
                "ReservationPolicy:MaximumOutstandingReservationsPerDriver must be a positive integer.")
            .Validate(
                options => options.MaximumBookingDuration >= TimeSpan.FromMinutes(1),
                "ReservationPolicy:MaximumBookingDuration must be at least one minute to match the booking picker.")
            .Validate(
                options => options.MaximumAdvanceBookingHorizon >= TimeSpan.FromMinutes(1),
                "ReservationPolicy:MaximumAdvanceBookingHorizon must be at least one minute to match the booking picker.")
            .Validate(
                options => options.ArrivalAllowance > TimeSpan.Zero,
                "ReservationPolicy:ArrivalAllowance must be positive.")
            .ValidateOnStart();

        services.AddScoped<ListStationsQueryHandler>();
        services.AddScoped<GetStationQueryHandler>();
        services.AddScoped<GetStationEditorQueryHandler>();
        services.AddScoped<UpdateStationCommandHandler>();
        services.AddScoped<ListAssignedStationsQueryHandler>();
        services.AddScoped<IAuthorizationHandler, StationAssignmentAuthorizationHandler>();

        services.AddScoped<CreateReservationCommandHandler>();
        services.AddScoped<ListReservationsQueryHandler>();
        services.AddScoped<GetReservationQueryHandler>();
        services.AddScoped<GetBookingPolicyQueryHandler>();

        return services;
    }
}
