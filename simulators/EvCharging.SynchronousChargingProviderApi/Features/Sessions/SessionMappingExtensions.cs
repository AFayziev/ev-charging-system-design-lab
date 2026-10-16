using EvCharging.SynchronousChargingProviderApi.Persistence;

namespace EvCharging.SynchronousChargingProviderApi.Features.Sessions;

internal static class SessionMappingExtensions
{
    public static SessionResponse ToResponse(this ProviderSession session) => new(
        session.PublicId,
        session.Reference,
        session.StationId,
        session.ChargerId,
        session.ConnectorId,
        session.StartedAt,
        session.PowerKw,
        SessionState.Charging);
}
