using EvCharging.Api.Authentication;
using EvCharging.Api.Features.Charging;
using EvCharging.Api.Features.Stations;
using EvCharging.Persistence.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.ErrorHandling;

namespace EvCharging.Api.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddOptions<ChargingStatusCheckOptions>()
            .BindConfiguration(ChargingProgressOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
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

    // Validation metadata is generated for the assembly that calls AddValidation, so the call lives here
    // for this assembly's endpoint parameter types.
    public static IServiceCollection AddApplicationValidation(this IServiceCollection services) =>
        services.AddValidation();

    public static WolverineOptions AddApplicationHandlers(this WolverineOptions options)
    {
        options.Policies.Add<UserContextPolicy>();

        // Framework authorization and bound options require their native DI activation.
        options.CodeGeneration.AlwaysUseServiceLocationFor<IAuthorizationService>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<IOptionsSnapshot<ReservationPolicyOptions>>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<IOptions<ChargingStatusCheckOptions>>();

        // Concurrent writers can deadlock on exclusion constraints. The aborted transaction is rerun inline,
        // where it sees the winner's committed row and reports the conflict.
        options.Policies.OnException(ExceptionExtensions.IsTransactionConflict)
            .RetryWithCooldown(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(250));

        return options;
    }
}
