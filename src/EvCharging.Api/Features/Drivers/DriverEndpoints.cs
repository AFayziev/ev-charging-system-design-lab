using EvCharging.Api.Errors;
using EvCharging.Contracts;
using FluentResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine;

namespace EvCharging.Api.Features.Drivers;

internal static class DriverEndpoints
{
    public static WebApplication MapDrivers(this WebApplication app)
    {
        app.MapPost("/drivers/profile", EnsureProfile)
            .RequireAuthorization(PlatformRole.Driver.ToClaimValue());
        return app;
    }

    private static async Task<Results<Ok<DriverRegistrationResponse>, ChallengeHttpResult, ForbidHttpResult>>
        EnsureProfile(IMessageBus bus, CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<DriverRegistrationResponse>>(
            new EnsureDriverProfileCommand(), cancellationToken);
        return result switch
        {
            _ when result.HasError<UnauthenticatedError>() => TypedResults.Challenge(),
            _ when result.HasError<IncompleteDriverRegistrationError>() => TypedResults.Forbid(),
            _ => TypedResults.Ok(result.Value)
        };
    }
}
