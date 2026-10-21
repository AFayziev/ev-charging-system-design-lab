using System.ComponentModel.DataAnnotations;
using EvCharging.Api.Authentication;
using EvCharging.Api.Errors;
using EvCharging.Api.Features.Reservations;
using EvCharging.Contracts.Observations;
using EvCharging.DocumentStore;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;
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

/// <summary>A session's lifecycle with its latest reading; the reading is absent until one has been projected.</summary>
public sealed record ChargingProgressResponse(
    Guid SessionId,
    SessionLifecycle Lifecycle,
    DateTimeOffset? StartedAt,
    ChargingReadingResponse? Reading,
    bool IsStale);

public sealed record GetChargingProgressRequest(Guid ReservationId) : IAuthenticatedRequest<ChargingProgressResponse>;

public sealed class ChargingProgressOptions
{
    public const string SectionName = "ChargingProgress";

    /// <summary>A reading older than this is stale, unless it is the provider's final one; silence says nothing about the session.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan FreshnessLimit { get; init; } = TimeSpan.FromSeconds(10);
}

internal static class ChargingProgressExtensions
{
    public static IHostApplicationBuilder AddChargingProgress(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ChargingProgressOptions>()
            .BindConfiguration(ChargingProgressOptions.SectionName)
            .ValidateDataAnnotations()
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
        var reservation = await database.LoadOwnedAsync(request.ReservationId, identity, cancellationToken);
        if (reservation.IsFailed)
        {
            return Result.Fail<ChargingProgressResponse>(reservation.Errors);
        }

        var session = await database.Sessions
            .AsNoTracking()
            .Where(candidate => candidate.ReservationId == reservation.Value.Id)
            .Select(candidate => new
                {
                    candidate.PublicId,
                    candidate.Lifecycle,
                    candidate.StartedAt,
                    // The start attempt names the integration before the provider has confirmed the session.
                    IntegrationKey = database.Attempts
                        .Where(attempt => attempt.Operation!.SessionId == candidate.Id)
                        .OrderBy(attempt => attempt.AuthorizedAt)
                        .Select(attempt => attempt.IntegrationKey)
                        .FirstOrDefault()
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return Result.Fail<ChargingProgressResponse>(
                new NotFoundError<Guid>("charging session", request.ReservationId));
        }

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

        return Result.Ok(new ChargingProgressResponse(
            session.PublicId,
            session.Lifecycle,
            session.StartedAt,
            reading is null
                ? null
                : new ChargingReadingResponse(
                    reading.State, reading.PowerKw, reading.EnergyKWh, reading.IsFinalEnergy, reading.OccurredAt),
            reading is { IsFinalEnergy: false }
                && clock.GetUtcNow() - reading.OccurredAt > options.Value.FreshnessLimit));
    }
}
