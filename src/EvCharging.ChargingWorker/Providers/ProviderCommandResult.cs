namespace EvCharging.ChargingWorker.Providers;

public enum ProviderCommandOutcome
{
    Applied,
    Rejected
}

public enum ProviderCommandKind
{
    Start,
    Stop
}

public sealed record ProviderCommandResult(
    Guid Id,
    Guid ExternalOperationId,
    ProviderCommandKind Kind,
    ProviderCommandOutcome Outcome,
    Guid? SessionId,
    DateTimeOffset? AppliedAt,
    string? Rejection);
