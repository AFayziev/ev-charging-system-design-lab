namespace EvCharging.WattHarborProviderApi.Features.Sessions;

internal sealed record SessionResponse(
    Guid Id,
    Guid ExternalSessionId,
    string StationId,
    string ChargerId,
    string ConnectorId,
    DateTimeOffset StartedAt,
    decimal PowerKw,
    SessionState State);
