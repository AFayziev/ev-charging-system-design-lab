namespace EvCharging.ChargingWorker.Providers;

internal enum ProviderCommandOutcome
{
    Applied,
    Rejected
}

internal sealed record ProviderCommandResult(
    Guid Id,
    Guid Reference,
    ProviderCommandOutcome Outcome,
    Guid? SessionId,
    DateTimeOffset? AppliedAt,
    string? Rejection);
