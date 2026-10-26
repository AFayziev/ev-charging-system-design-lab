namespace EvCharging.SearchApi.Features.Stations;

/// <summary>What the index holds to filter on, each sorted, so a client offers exactly what can match.</summary>
public sealed record StationSearchOptionsResponse(
    IReadOnlyList<string> ConnectorTypes,
    IReadOnlyList<string> PowerSupplies,
    IReadOnlyList<decimal> RatedPowersKw);
