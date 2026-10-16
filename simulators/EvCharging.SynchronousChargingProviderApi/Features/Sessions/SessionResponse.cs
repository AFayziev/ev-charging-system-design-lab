namespace EvCharging.SynchronousChargingProviderApi.Features.Sessions;

internal sealed record SessionResponse(
    Guid Id,
    Guid Reference,
    string StationId,
    string ChargerId,
    string ConnectorId,
    DateTimeOffset StartedAt,
    decimal PowerKw,
    SessionState State);
