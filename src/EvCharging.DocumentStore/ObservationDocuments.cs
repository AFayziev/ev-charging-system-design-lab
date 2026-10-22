using EvCharging.Contracts.Observations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace EvCharging.DocumentStore;

/// <summary>Identifies one provider event; the history keeps a single document per event.</summary>
public sealed class ObservationEventKey
{
    public required string IntegrationKey { get; init; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid SourceEventId { get; init; }
}

/// <summary>Identifies one provider session; the latest view keeps a single document per session.</summary>
public sealed class ObservationSessionKey
{
    public required string IntegrationKey { get; init; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid ExternalSessionId { get; init; }
}

/// <summary>What one observation reported, as stored in both views.</summary>
public sealed class ObservationReading
{
    public required string StationId { get; init; }
    public required string ChargerId { get; init; }
    public required string ConnectorId { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public required DateTimeOffset OccurredAt { get; init; }

    [BsonRepresentation(BsonType.DateTime)]
    public required DateTimeOffset ReceivedAt { get; init; }

    [BsonRepresentation(BsonType.String)]
    public required ObservationState State { get; init; }

    [BsonRepresentation(BsonType.Decimal128)]
    public required decimal PowerKw { get; init; }

    [BsonRepresentation(BsonType.Decimal128)]
    public required decimal EnergyKWh { get; init; }

    public required bool IsFinalEnergy { get; init; }

    public static ObservationReading From(ObservationEnvelope envelope) => new()
    {
        StationId = envelope.StationId,
        ChargerId = envelope.ChargerId,
        ConnectorId = envelope.ConnectorId,
        OccurredAt = envelope.OccurredAt,
        ReceivedAt = envelope.ReceivedAt,
        State = envelope.State,
        PowerKw = envelope.PowerKw,
        EnergyKWh = envelope.EnergyKWh,
        IsFinalEnergy = envelope.IsFinalEnergy
    };
}

/// <summary>An accepted observation, kept regardless of how it orders against other readings.</summary>
public sealed class ObservationHistoryDocument
{
    [BsonId]
    public required ObservationEventKey Id { get; init; }

    // Repeated from the key so the by-session index does not reach into it.
    public required string IntegrationKey { get; init; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid ExternalSessionId { get; init; }

    public required ObservationReading Reading { get; init; }

    public static ObservationHistoryDocument From(ObservationEnvelope envelope) => new()
    {
        Id = new ObservationEventKey
        {
            IntegrationKey = envelope.IntegrationKey,
            SourceEventId = envelope.SourceEventId
        },
        IntegrationKey = envelope.IntegrationKey,
        ExternalSessionId = envelope.ExternalSessionId,
        Reading = ObservationReading.From(envelope)
    };
}

/// <summary>The reading with the newest source occurrence time that is not dated ahead of its receipt.</summary>
public sealed class SessionLatestDocument
{
    [BsonId]
    public required ObservationSessionKey Id { get; init; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public required Guid SourceEventId { get; init; }

    public required ObservationReading Reading { get; init; }

    public static SessionLatestDocument From(ObservationEnvelope envelope) => new()
    {
        Id = new ObservationSessionKey
        {
            IntegrationKey = envelope.IntegrationKey,
            ExternalSessionId = envelope.ExternalSessionId
        },
        SourceEventId = envelope.SourceEventId,
        Reading = ObservationReading.From(envelope)
    };
}
