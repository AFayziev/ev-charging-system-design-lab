using EvCharging.WattHarborProviderApi.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;

namespace EvCharging.WattHarborProviderApi.Features.Simulation;

/// <summary>Delivery faults for one session's observations. Observations are still recorded and in the snapshot.</summary>
/// <param name="Duplicate">Send each observation twice with the same event ID.</param>
/// <param name="DeliverOutOfOrder">Send a newer observation before the one it follows.</param>
/// <param name="Interrupt">Do not send observations, including the terminal one.</param>
public sealed record ConfigureTelemetryFaultRequest(bool Duplicate, bool DeliverOutOfOrder, bool Interrupt);

internal sealed class TelemetryFaults(IMemoryCache cache)
{
    public void Configure(Guid externalSessionId, ConfigureTelemetryFaultRequest request) =>
        cache.Set((nameof(TelemetryFaults), externalSessionId), request, TimeSpan.FromMinutes(10));

    public ConfigureTelemetryFaultRequest? Find(Guid externalSessionId) =>
        cache.Get<ConfigureTelemetryFaultRequest>((nameof(TelemetryFaults), externalSessionId));
}

internal static class TelemetryFaultEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryFaultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/simulation/sessions/{externalSessionId:guid}/telemetry", Configure)
            .RequireAuthorization(ProviderAuthenticationExtensions.SimulationControlPolicy);
        return endpoints;
    }

    private static Results<NoContent, ValidationProblem> Configure(
        Guid externalSessionId, ConfigureTelemetryFaultRequest request, TelemetryFaults faults)
    {
        if (externalSessionId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(externalSessionId)] = ["A non-empty external session ID is required."]
            });
        }

        faults.Configure(externalSessionId, request);
        return TypedResults.NoContent();
    }
}
