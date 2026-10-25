namespace EvCharging.SearchIndex;

public sealed record ConnectorSearchDocument(string ConnectorType, string PowerSupply, decimal RatedPowerKW);
