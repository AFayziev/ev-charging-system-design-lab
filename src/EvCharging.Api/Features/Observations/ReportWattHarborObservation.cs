using System.ComponentModel.DataAnnotations;
using EvCharging.Api.Streaming;
using EvCharging.Contracts.Observations;
using FluentResults;

namespace EvCharging.Api.Features.Observations;

/// <summary>A telemetry observation reported by a WattHarbor session.</summary>
public sealed record WattHarborObservationRequest : IValidatableObject
{
    private const int MaximumIdentifierLength = 128;

    /// <summary>The provider event identity; a redelivery repeats it.</summary>
    public required Guid EventId { get; init; }

    /// <summary>The session identity the platform sent when the session was started.</summary>
    public required Guid ExternalSessionId { get; init; }

    [Required, StringLength(MaximumIdentifierLength, MinimumLength = 1)]
    public required string StationId { get; init; }

    [Required, StringLength(MaximumIdentifierLength, MinimumLength = 1)]
    public required string ChargerId { get; init; }

    [Required, StringLength(MaximumIdentifierLength, MinimumLength = 1)]
    public required string ConnectorId { get; init; }

    /// <summary>The instant the provider measured the values. It is the only ordering field.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    public required WattHarborObservationState State { get; init; }

    /// <summary>Instantaneous power in kilowatts.</summary>
    [Range(0d, double.MaxValue)]
    public required decimal PowerKw { get; init; }

    /// <summary>Cumulative session energy in watt-hours.</summary>
    [Range(0d, double.MaxValue)]
    public required decimal EnergyWh { get; init; }

    /// <summary>Whether <see cref="EnergyWh"/> is the explicitly final reading of an ended session.</summary>
    public required bool IsFinalEnergy { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EventId == Guid.Empty)
        {
            yield return new ValidationResult("An event identifier is required.", [nameof(EventId)]);
        }

        if (ExternalSessionId == Guid.Empty)
        {
            yield return new ValidationResult("A session identifier is required.", [nameof(ExternalSessionId)]);
        }

        if (OccurredAt == default)
        {
            yield return new ValidationResult("An occurrence time is required.", [nameof(OccurredAt)]);
        }

        if (IsFinalEnergy && State != WattHarborObservationState.Stopped)
        {
            yield return new ValidationResult("Final energy requires the stopped state.", [nameof(IsFinalEnergy)]);
        }
    }
}

public enum WattHarborObservationState
{
    Charging,
    Stopped
}

public sealed record ObservationAcceptedResponse(Guid ObservationId);

public sealed record ReportWattHarborObservationCommand(
    string IntegrationKey,
    WattHarborObservationRequest Observation,
    string RawPayload);

internal sealed class ObservationStreamUnavailableError()
    : Error("The observation stream did not confirm the append.");

public sealed class ReportWattHarborObservationCommandHandler(
    IObservationStream stream,
    TimeProvider clock,
    ILogger<ReportWattHarborObservationCommandHandler> logger)
{
    public async Task<Result<ObservationAcceptedResponse>> HandleAsync(
        ReportWattHarborObservationCommand command, CancellationToken cancellationToken)
    {
        var envelope = command.ToEnvelope(Guid.CreateVersion7(), clock.GetUtcNow());
        try
        {
            await stream.AppendAsync(envelope, cancellationToken);
        }
        catch (ObservationStreamException exception)
        {
            logger.LogWarning(
                exception,
                "Observation {SourceEventId} from {IntegrationKey} was not confirmed by the stream.",
                envelope.SourceEventId,
                envelope.IntegrationKey);
            return Result.Fail<ObservationAcceptedResponse>(new ObservationStreamUnavailableError());
        }

        return Result.Ok(new ObservationAcceptedResponse(envelope.ObservationId));
    }
}

internal static class ObservationMappings
{
    private const decimal WattHoursPerKilowattHour = 1000m;

    public static ObservationEnvelope ToEnvelope(
        this ReportWattHarborObservationCommand command, Guid observationId, DateTimeOffset receivedAt)
    {
        var observation = command.Observation;
        return new ObservationEnvelope(
            ObservationEnvelope.CurrentSchemaVersion,
            observationId,
            command.IntegrationKey,
            observation.EventId,
            observation.ExternalSessionId,
            observation.StationId,
            observation.ChargerId,
            observation.ConnectorId,
            observation.OccurredAt.ToUniversalTime(),
            receivedAt.ToUniversalTime(),
            observation.State.ToObservationState(),
            observation.PowerKw,
            observation.EnergyWh / WattHoursPerKilowattHour,
            observation.IsFinalEnergy,
            command.RawPayload);
    }

    private static ObservationState ToObservationState(this WattHarborObservationState state) => state switch
    {
        WattHarborObservationState.Charging => ObservationState.Charging,
        WattHarborObservationState.Stopped => ObservationState.Stopped,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown WattHarbor observation state.")
    };
}
