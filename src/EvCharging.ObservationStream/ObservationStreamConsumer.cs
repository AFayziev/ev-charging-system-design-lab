using System.Text.Json;
using Confluent.Kafka;
using EvCharging.Contracts.Observations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace EvCharging.ObservationStream;

/// <summary>
/// Reads the observation stream in batches and hands each batch to its handler. An offset is stored only after its
/// batch is handled, so a restart resumes from the last handled message.
/// </summary>
internal sealed class ObservationStreamConsumer(
    IConsumer<string, string> consumer,
    IObservationBatchHandler handler,
    IOptions<ObservationStreamOptions> stream,
    IOptions<ObservationProcessingOptions> options,
    ResiliencePipelineProvider<string> pipelines,
    TimeProvider clock,
    ILogger<ObservationStreamConsumer> logger) : BackgroundService
{
    private readonly ResiliencePipeline _pipeline = pipelines.GetPipeline(ObservationResilience.PipelineKey);

    // Consume blocks the calling thread, so the loop runs apart from host startup.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), CancellationToken.None);

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            consumer.Subscribe(stream.Value.Topic);
            while (!cancellationToken.IsCancellationRequested)
            {
                var messages = ReadBatch(cancellationToken);
                if (messages.Count == 0)
                {
                    continue;
                }

                await HandleUntilStoredAsync(DeserializeValid(messages), cancellationToken);
                StoreOffsets(messages);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            // Commits the stored offsets and leaves the group.
            consumer.Close();
        }
    }

    private List<ConsumeResult<string, string>> ReadBatch(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var messages = new List<ConsumeResult<string, string>>();
        long started = 0;
        while (messages.Count < settings.BatchSize)
        {
            // Wait for the first message indefinitely, then only for what is left of the batch window.
            var result = messages.Count == 0
                ? _pipeline.Execute(token => consumer.Consume(token), cancellationToken)
                : ConsumeWithin(Remaining(started, settings.BatchWait));
            if (result is null)
            {
                break;
            }

            if (messages.Count == 0)
            {
                started = clock.GetTimestamp();
            }

            messages.Add(result);
        }

        return messages;
    }

    // A read error ends the batch collected so far; the next blocking read retries with backoff.
    private ConsumeResult<string, string>? ConsumeWithin(TimeSpan window)
    {
        try
        {
            return consumer.Consume(window);
        }
        catch (ConsumeException exception) when (!exception.Error.IsFatal)
        {
            logger.LogWarning(exception, "Consuming the observation stream failed; the batch ends early.");
            return null;
        }
    }

    private TimeSpan Remaining(long started, TimeSpan window)
    {
        var remaining = window - clock.GetElapsedTime(started);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private List<ObservationEnvelope> DeserializeValid(List<ConsumeResult<string, string>> messages) =>
        messages.Select(TryDeserialize).OfType<ObservationEnvelope>().ToList();

    private ObservationEnvelope? TryDeserialize(ConsumeResult<string, string> message)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<ObservationEnvelope>(
                message.Message.Value, ObservationEnvelope.SerializerOptions);
            if (envelope is { SchemaVersion: ObservationEnvelope.CurrentSchemaVersion } && IsComplete(envelope))
            {
                return envelope;
            }

            logger.LogError(
                "Skipping {TopicPartitionOffset}: unsupported or incomplete observation envelope (schema version {SchemaVersion}).",
                message.TopicPartitionOffset,
                envelope?.SchemaVersion);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentNullException)
        {
            logger.LogError(
                exception,
                "Skipping {TopicPartitionOffset}: the message is not a valid envelope.",
                message.TopicPartitionOffset);
        }

        return null;
    }

    private ValueTask HandleUntilStoredAsync(
        List<ObservationEnvelope> envelopes,
        CancellationToken cancellationToken) =>
        _pipeline.ExecuteAsync(
            token => new ValueTask(handler.HandleAsync(envelopes, token)),
            cancellationToken);

    private void StoreOffsets(List<ConsumeResult<string, string>> messages)
    {
        foreach (var message in messages)
        {
            try
            {
                consumer.StoreOffset(message);
            }
            catch (KafkaException exception)
            {
                // The partition was reassigned while the batch was retried; its new owner reads the batch again,
                // and handling is idempotent.
                logger.LogWarning(exception, "Could not store {TopicPartitionOffset}.", message.TopicPartitionOffset);
            }
        }
    }

    private static bool IsComplete(ObservationEnvelope envelope) =>
        !string.IsNullOrWhiteSpace(envelope.IntegrationKey)
        && !string.IsNullOrWhiteSpace(envelope.StationId)
        && !string.IsNullOrWhiteSpace(envelope.ChargerId)
        && !string.IsNullOrWhiteSpace(envelope.ConnectorId)
        && envelope.SourceEventId != Guid.Empty
        && envelope.ExternalSessionId != Guid.Empty
        && envelope.OccurredAt != default
        && envelope.ReceivedAt != default;
}
