using System.ComponentModel.DataAnnotations;
using EvCharging.Contracts.Observations;
using EvCharging.ObservationStream;
using FluentResults;
using Microsoft.Extensions.Options;

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

/// <summary>Observations reported together. They are accepted or rejected as a whole.</summary>
public sealed record WattHarborObservationBatchRequest : IValidatableObject
{
    [Required, MinLength(1)]
    public required IReadOnlyList<WattHarborObservationRequest> Observations { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Observations.Any(observation => observation is null))
        {
            yield return new ValidationResult("Every observation must be an object.", [nameof(Observations)]);
        }

        var options = validationContext.GetRequiredService<IOptions<ObservationIngestionOptions>>().Value;
        if (Observations.Count > options.MaximumBatchSize)
        {
            yield return new ValidationResult(
                $"At most {options.MaximumBatchSize} observations can be reported together.",
                [nameof(Observations)]);
        }
    }
}

public sealed record ObservationsAcceptedResponse(IReadOnlyList<Guid> ObservationIds);

public sealed record ReportWattHarborObservationsCommand(
    string IntegrationKey,
    IReadOnlyList<WattHarborObservationRequest> Observations);

internal sealed class ObservationStreamUnavailableError()
    : Error("The observation stream did not confirm the append.");

public sealed class ReportWattHarborObservationsCommandHandler(
    IObservationStreamProducer stream,
    TimeProvider clock,
    ILogger<ReportWattHarborObservationsCommandHandler> logger)
{
    public async Task<Result<ObservationsAcceptedResponse>> HandleAsync(
        ReportWattHarborObservationsCommand command, CancellationToken cancellationToken)
    {
        var receivedAt = clock.GetUtcNow();
        var envelopes = command.Observations
            .Select(observation => observation.ToEnvelope(command.IntegrationKey, Guid.CreateVersion7(), receivedAt))
            .ToArray();
        try
        {
            await stream.AppendAsync(envelopes, cancellationToken);
        }
        catch (ObservationStreamException exception)
        {
            logger.LogWarning(
                exception,
                "{Count} observations from {IntegrationKey} were not confirmed by the stream.",
                envelopes.Length,
                command.IntegrationKey);
            return Result.Fail<ObservationsAcceptedResponse>(new ObservationStreamUnavailableError());
        }

        var observationIds = envelopes.Select(envelope => envelope.ObservationId).ToArray();
        return Result.Ok(new ObservationsAcceptedResponse(observationIds));
    }
}

internal static class ObservationMappings
{
    private const decimal WattHoursPerKilowattHour = 1000m;

    public static ObservationEnvelope ToEnvelope(
        this WattHarborObservationRequest observation,
        string integrationKey,
        Guid observationId,
        DateTimeOffset receivedAt)
    {
        return new ObservationEnvelope(
            ObservationEnvelope.CurrentSchemaVersion,
            observationId,
            integrationKey,
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
            observation.IsFinalEnergy);
    }

    private static ObservationState ToObservationState(this WattHarborObservationState state) => state switch
    {
        WattHarborObservationState.Charging => ObservationState.Charging,
        WattHarborObservationState.Stopped => ObservationState.Stopped,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown WattHarbor observation state.")
    };
}
