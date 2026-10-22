using EvCharging.Api.Authentication;
using EvCharging.DocumentStore;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Features.Charging;

/// <summary>The average and peak power of a session over one bucket of source time.</summary>
public sealed record ChargingPowerBucketResponse(
    DateTimeOffset StartsAt, decimal AveragePowerKw, decimal MaximumPowerKw, decimal LastEnergyKWh);

/// <summary>Power history in one-minute buckets of source time, oldest first.</summary>
public sealed record ChargingPowerResponse(IReadOnlyList<ChargingPowerBucketResponse> Buckets);

/// <param name="From">Returns the buckets from the one holding this instant; all of them when absent.</param>
public sealed record GetChargingPowerRequest(Guid SessionId, DateTimeOffset? From)
    : IAuthenticatedRequest<ChargingPowerResponse>;

public sealed class GetChargingPowerQueryHandler(
    PlatformDbContext database,
    ObservationStore observations,
    IOptions<ObservationClockSkewOptions> clockSkew,
    TimeProvider clock)
{
    public async Task<Result<ChargingPowerResponse>> HandleAsync(
        GetChargingPowerRequest request, UserIdentity identity, CancellationToken cancellationToken)
    {
        var loaded = await database.LoadOwnedSessionAsync(request.SessionId, identity, cancellationToken);
        if (loaded.IsFailed)
        {
            return Result.Fail<ChargingPowerResponse>(loaded.Errors);
        }

        var session = loaded.Value;
        if (session.IntegrationKey is null)
        {
            return Result.Ok(new ChargingPowerResponse([]));
        }

        // A reading dated further ahead of the clock than the skew is not trusted, so it is not charted.
        var buckets = await observations.GetPowerBucketsAsync(
            session.IntegrationKey,
            session.PublicId,
            request.From,
            clock.GetUtcNow() + clockSkew.Value.ClockSkew,
            cancellationToken);
        return Result.Ok(new ChargingPowerResponse(
            buckets.Select(bucket => new ChargingPowerBucketResponse(
                bucket.StartsAt, bucket.AveragePowerKw, bucket.MaximumPowerKw, bucket.LastEnergyKWh))
                .ToList()));
    }
}
