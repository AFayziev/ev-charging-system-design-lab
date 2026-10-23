using System.ComponentModel.DataAnnotations;
using EvCharging.Api.Authentication;
using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace EvCharging.Api.Features.Charging;

/// <summary>The newest provider reading of a session, as reported by the provider.</summary>
public sealed record ChargingReadingResponse(
    ObservationState State,
    decimal PowerKw,
    decimal EnergyKWh,
    bool IsFinalEnergy,
    DateTimeOffset OccurredAt);

/// <summary>
/// A session's lifecycle with its latest reading; the reading is absent until one has been projected. A completed
/// session also carries its frozen final usage and end time.
/// </summary>
public sealed record ChargingProgressResponse(
    Guid SessionId,
    Guid ReservationId,
    SessionLifecycle Lifecycle,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    decimal? FinalEnergyKWh,
    ChargingReadingResponse? Reading,
    bool IsStale,
    bool IsSilentPastLimit,
    double? ElapsedSeconds);

public sealed record GetChargingProgressRequest(Guid SessionId) : IAuthenticatedRequest<ChargingProgressResponse>;

public sealed class ChargingProgressOptions
{
    public const string SectionName = "Charging";

    /// <summary>A reading older than this is stale, unless it is the provider's final one; silence says nothing about the session.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan FreshnessLimit { get; init; }

    /// <summary>A running session silent for longer than this is no longer worth polling; it is refreshed by hand.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan SilenceLimit { get; init; }
}

internal static class ChargingProgressExtensions
{
    public static IHostApplicationBuilder AddChargingProgress(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ChargingProgressOptions>()
            .BindConfiguration(ChargingProgressOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.SilenceLimit > options.FreshnessLimit,
                "The silence limit must be longer than the freshness limit.")
            .ValidateOnStart();
        return builder;
    }
}

public sealed class GetChargingProgressQueryHandler(
    PlatformDbContext database,
    ObservationStore observations,
    IOptions<ChargingProgressOptions> options,
    TimeProvider clock)
{
    public async Task<Result<ChargingProgressResponse>> HandleAsync(
        GetChargingProgressRequest request, UserIdentity identity, CancellationToken cancellationToken)
    {
        var loaded = await database.LoadOwnedSessionAsync(request.SessionId, identity, cancellationToken);
        if (loaded.IsFailed)
        {
            return Result.Fail<ChargingProgressResponse>(loaded.Errors);
        }

        var session = loaded.Value;
        // Matching the whole key lets the lookup use the _id index.
        var reading = session.IntegrationKey is null
            ? null
            : await observations.Latest
                .Find(Builders<SessionLatestDocument>.Filter.Eq(
                    document => document.Id,
                    new ObservationSessionKey
                    {
                        IntegrationKey = session.IntegrationKey,
                        ExternalSessionId = session.PublicId
                    }))
                .Project(document => document.Reading)
                .FirstOrDefaultAsync(cancellationToken);

        var now = clock.GetUtcNow();
        // A started session that has reported nothing has been quiet since it started.
        var lastHeardAt = reading?.OccurredAt ?? session.StartedAt;
        var isFinal = reading is { IsFinalEnergy: true };
        return Result.Ok(new ChargingProgressResponse(
            session.PublicId,
            session.ReservationId,
            session.Lifecycle,
            session.StartedAt,
            session.CompletedAt,
            session.FinalEnergyKWh,
            reading is null
                ? null
                : new ChargingReadingResponse(
                    reading.State, reading.PowerKw, reading.EnergyKWh, reading.IsFinalEnergy, reading.OccurredAt),
            SessionTiming.IsSilentFor(session.Lifecycle, lastHeardAt, isFinal, now, options.Value.FreshnessLimit),
            SessionTiming.IsSilentFor(session.Lifecycle, lastHeardAt, isFinal, now, options.Value.SilenceLimit),
            SessionTiming.Elapsed(
                session.Lifecycle, session.StartedAt, session.CompletedAt ?? reading?.OccurredAt, isFinal, now)
                ?.TotalSeconds));
    }
}
