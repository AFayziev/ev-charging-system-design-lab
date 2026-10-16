namespace EvCharging.ChargingWorker.Providers;

public enum ProviderCommandOutcome
{
    Applied,
    Rejected
}

public sealed record ProviderCommandResult(
    Guid Id,
    Guid ExternalOperationId,
    ProviderCommandOutcome Outcome,
    Guid? SessionId,
    DateTimeOffset? AppliedAt,
    string? Rejection);
