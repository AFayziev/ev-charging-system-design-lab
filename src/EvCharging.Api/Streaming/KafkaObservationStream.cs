using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using EvCharging.Contracts.Observations;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Streaming;

internal sealed class KafkaObservationStream(
    IProducer<string, string> producer,
    IOptions<ObservationStreamOptions> options) : IObservationStream
{
    private static readonly JsonSerializerOptions EnvelopeJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task AppendAsync(IReadOnlyList<ObservationEnvelope> envelopes, CancellationToken cancellationToken)
    {
        try
        {
            // Every produce call is made before any is awaited, so each partition keeps the input order.
            var deliveries = envelopes.Select(envelope => ProduceAsync(envelope, cancellationToken)).ToArray();
            var results = await Task.WhenAll(deliveries);
            if (results.FirstOrDefault(delivery => delivery.Status != PersistenceStatus.Persisted) is { } unconfirmed)
            {
                throw new ObservationStreamException(
                    "The observation stream did not confirm the append.",
                    new InvalidOperationException($"Delivery status was {unconfirmed.Status}."));
            }
        }
        catch (KafkaException exception)
        {
            throw new ObservationStreamException("The observation stream rejected the append.", exception);
        }
    }

    private Task<DeliveryResult<string, string>> ProduceAsync(
        ObservationEnvelope envelope, CancellationToken cancellationToken) =>
        producer.ProduceAsync(
            options.Value.Topic,
            new Message<string, string>
            {
                // One partition per connector keeps a connector's observations in receipt order. The key is a
                // JSON array so provider identifiers containing separators cannot produce the same key.
                Key = JsonSerializer.Serialize(
                    new[] { envelope.IntegrationKey, envelope.StationId, envelope.ChargerId, envelope.ConnectorId }),
                Value = JsonSerializer.Serialize(envelope, EnvelopeJson)
            },
            cancellationToken);
}
