using EvCharging.Contracts.Observations;

namespace EvCharging.Api.Streaming;

/// <summary>The durable acceptance boundary for observations.</summary>
public interface IObservationStream
{
    /// <summary>Appends the envelopes and returns only after the stream confirms every one is persisted.</summary>
    /// <exception cref="ObservationStreamException">The append was not confirmed. Some envelopes may still
    /// have been written, so a retry can produce duplicates that consumers deduplicate by source event.</exception>
    Task AppendAsync(IReadOnlyList<ObservationEnvelope> envelopes, CancellationToken cancellationToken);
}
