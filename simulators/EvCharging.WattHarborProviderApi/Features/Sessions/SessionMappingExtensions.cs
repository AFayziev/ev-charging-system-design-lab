using EvCharging.WattHarborProviderApi.Persistence;

namespace EvCharging.WattHarborProviderApi.Features.Sessions;

internal static class SessionMappingExtensions
{
    public static SessionResponse ToResponse(this ProviderSession session) => new(
        session.PublicId,
        session.ExternalSessionId,
        session.StationId,
        session.ChargerId,
        session.ConnectorId,
        session.StartedAt,
        session.PowerKw,
        SessionState.Charging);
}
