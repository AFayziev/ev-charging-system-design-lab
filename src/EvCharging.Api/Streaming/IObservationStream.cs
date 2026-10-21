using EvCharging.Contracts.Observations;

namespace EvCharging.Api.Streaming;

/// <summary>The durable acceptance boundary for observations.</summary>
public interface IObservationStream
{
    /// <summary>Appends the envelope and returns only after the stream confirms it is persisted.</summary>
    /// <exception cref="ObservationStreamException">The append was not confirmed. The envelope may still
    /// have been written, so a retry can produce a duplicate that consumers deduplicate by source event.</exception>
    Task AppendAsync(ObservationEnvelope envelope, CancellationToken cancellationToken);
}
