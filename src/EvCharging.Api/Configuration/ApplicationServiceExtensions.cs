using EvCharging.Api.Authentication;
using EvCharging.Api.Features.Stations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Wolverine;

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

        services.AddScoped<IAuthorizationHandler, StationAssignmentAuthorizationHandler>();

        return services;
    }

    public static WolverineOptions AddApplicationHandlers(this WolverineOptions options)
    {
        options.Policies.Add<UserContextPolicy>();

        // Framework authorization and bound options require their native DI activation.
        options.CodeGeneration.AlwaysUseServiceLocationFor<IAuthorizationService>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<IOptionsSnapshot<ReservationPolicyOptions>>();

        return options;
    }
}
