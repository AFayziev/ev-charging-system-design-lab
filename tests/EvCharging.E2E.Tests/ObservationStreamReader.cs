using System.Text.Json;
using Confluent.Kafka;

namespace EvCharging.E2E.Tests;

/// <summary>Reads the observation topic from the beginning with its own consumer group, committing nothing.</summary>
internal static class ObservationStreamReader
{
    public static async Task<IReadOnlyList<JsonElement>> ReadUntilAsync(
        string bootstrapServers,
        string topic,
        Guid externalSessionId,
        Func<IReadOnlyList<JsonElement>, bool> condition,
        CancellationToken cancellationToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"e2e-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        })
            .Build();
        consumer.Subscribe(topic);
        var matching = new List<JsonElement>();
        while (!condition(matching))
        {
            var result = await Task.Run(() => consumer.Consume(TimeSpan.FromMilliseconds(500)), cancellationToken);
            if (result is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }

            using var document = JsonDocument.Parse(result.Message.Value);
            var envelope = document.RootElement.Clone();
            if (envelope.GetProperty("externalSessionId").GetGuid() == externalSessionId)
            {
                matching.Add(envelope);
            }
        }

        return matching;
    }
}
