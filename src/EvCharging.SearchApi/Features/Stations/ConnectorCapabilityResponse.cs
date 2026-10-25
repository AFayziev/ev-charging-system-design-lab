namespace EvCharging.SearchApi.Features.Stations;

public sealed record ConnectorCapabilityResponse(string ConnectorType, string PowerSupply, decimal RatedPowerKW);
