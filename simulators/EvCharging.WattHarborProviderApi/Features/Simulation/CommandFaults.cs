using EvCharging.WattHarborProviderApi.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;

namespace EvCharging.WattHarborProviderApi.Features.Simulation;

internal sealed record ConfigureCommandFaultRequest(
    Guid ExternalSessionId,
    bool LoseStartResponse,
    int? LookupStatusCode);

internal sealed class CommandFaults(IMemoryCache cache)
{
    public void Configure(Guid operationId, ConfigureCommandFaultRequest request)
    {
        var lifetime = TimeSpan.FromMinutes(10);
        cache.Set((nameof(CommandFaults), operationId), request, lifetime);
        cache.Set((nameof(CommandFaults), request.ExternalSessionId), request, lifetime);
    }

    public ConfigureCommandFaultRequest? Find(Guid id) =>
        cache.Get<ConfigureCommandFaultRequest>((nameof(CommandFaults), id));
}

internal static class CommandFaultEndpoints
{
    public static IEndpointRouteBuilder MapCommandFaultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/simulation/commands/{externalOperationId:guid}", Configure)
            .RequireAuthorization(ProviderAuthenticationExtensions.SimulationControlPolicy);
        return endpoints;
    }

    private static Results<NoContent, ValidationProblem> Configure(
        Guid externalOperationId, ConfigureCommandFaultRequest request, CommandFaults faults)
    {
        bool supportedLookupStatus = request.LookupStatusCode is null
            or StatusCodes.Status404NotFound
            or StatusCodes.Status503ServiceUnavailable
            or StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden;
        if (externalOperationId == Guid.Empty || request.ExternalSessionId == Guid.Empty || !supportedLookupStatus)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request)]
                    = ["Non-empty correlation IDs and a lookup status of 404, 503, 401, or 403 are required."]
            });
        }

        faults.Configure(externalOperationId, request);
        return TypedResults.NoContent();
    }
}
