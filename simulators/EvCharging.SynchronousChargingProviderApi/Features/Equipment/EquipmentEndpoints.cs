using System.ComponentModel.DataAnnotations;
using EvCharging.SynchronousChargingProviderApi.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EvCharging.SynchronousChargingProviderApi.Features.Equipment;

internal static class EquipmentEndpoints
{
    private const string ConnectorPath = "/stations/{stationId}/chargers/{chargerId}/connectors/{connectorId}";

    public static IEndpointRouteBuilder MapEquipmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ConnectorPath, GetAsync);
        endpoints.MapPut("/simulation" + ConnectorPath, ConfigureAsync)
            .RequireAuthorization(ProviderAuthenticationExtensions.SimulationControlPolicy);
        return endpoints;
    }

    private static async Task<Results<Ok<ConnectorResponse>, NotFound>> GetAsync(
        string stationId,
        string chargerId,
        string connectorId,
        GetConnectorQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var connector = await handler.HandleAsync(stationId, chargerId, connectorId, cancellationToken);
        return connector is null ? TypedResults.NotFound() : TypedResults.Ok(connector);
    }

    private static async Task<NoContent> ConfigureAsync(
        [Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string stationId,
        [Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string chargerId,
        [Required, StringLength(ProviderContractLimits.EquipmentIdentifierLength)] string connectorId,
        ConfigureConnectorRequest request,
        ConfigureConnectorCommandHandler handler,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(stationId, chargerId, connectorId, request, cancellationToken);
        return TypedResults.NoContent();
    }
}
