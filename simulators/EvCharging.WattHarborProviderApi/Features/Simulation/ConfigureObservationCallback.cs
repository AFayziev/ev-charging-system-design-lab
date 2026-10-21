using System.ComponentModel.DataAnnotations;
using EvCharging.WattHarborProviderApi.Authentication;
using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.WattHarborProviderApi.Features.Simulation;

/// <summary>Where observations are delivered and the key that authenticates the callbacks.</summary>
public sealed record ConfigureObservationCallbackRequest(
    [property: Required, StringLength(2048)] string Url,
    [property: Required, StringLength(256)] string ApiKey) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            yield return new ValidationResult("An absolute http or https URL is required.", [nameof(Url)]);
        }
    }
}

internal sealed class ConfigureObservationCallbackCommandHandler(ProviderDbContext database)
{
    public async Task HandleAsync(ConfigureObservationCallbackRequest request, CancellationToken cancellationToken) =>
        await database.SaveCallbackTargetAsync(
            new CallbackTarget { Url = request.Url, ApiKey = request.ApiKey },
            cancellationToken);
}

internal static class ObservationCallbackEndpoints
{
    public static IEndpointRouteBuilder MapObservationCallbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/simulation/callbacks/observations", ConfigureAsync)
            .RequireAuthorization(ProviderAuthenticationExtensions.SimulationControlPolicy);
        return endpoints;
    }

    private static async Task<NoContent> ConfigureAsync(
        ConfigureObservationCallbackRequest request,
        ConfigureObservationCallbackCommandHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(request, cancellationToken);
        return TypedResults.NoContent();
    }
}
