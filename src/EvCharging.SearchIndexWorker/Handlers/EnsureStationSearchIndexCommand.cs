namespace EvCharging.SearchIndexWorker.Handlers;

/// <summary>Builds the station index when it is missing, outdated, or out of step with the primary.</summary>
public sealed record EnsureStationSearchIndexCommand;
