using EvCharging.Contracts.Observations;

namespace EvCharging.ObservationStream;

/// <summary>
/// Applies a batch of observations to one store. Handling must be idempotent: a batch that failed part-way, or is
/// delivered again, converges to the same state when it is handled again.
/// </summary>
public interface IObservationBatchHandler
{
    Task HandleAsync(IReadOnlyList<ObservationEnvelope> envelopes, CancellationToken cancellationToken);
}
