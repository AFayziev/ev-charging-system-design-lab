using EvCharging.WattHarborProviderApi.Features.Simulation;
using EvCharging.WattHarborProviderApi.Persistence;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EvCharging.WattHarborProviderApi.Features.Telemetry;

/// <summary>
/// Delivers recorded observations to the configured callback target, one batch per poll. A failed batch is retried
/// with the same event identities until it succeeds, runs out of attempts or ages past the delivery horizon.
/// </summary>
internal sealed class ObservationDeliverer(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    TelemetryFaults faults,
    IOptions<TelemetryOptions> options,
    IOptions<JsonOptions> json,
    TimeProvider clock,
    ILogger<ObservationDeliverer> logger) : BackgroundService
{
    public const string HttpClientName = "observation-callbacks";
    public const string ApiKeyHeader = "X-Api-Key";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.DeliveryPollInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DeliverDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Delivering session observations failed; the next poll retries.");
            }
        }
    }

    private async Task DeliverDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
        var now = clock.GetUtcNow();
        var oldest = now - settings.DeliveryHorizon;
        await database.Observations
            .Where(observation => observation.DeliveryStatus == ObservationDeliveryStatus.Pending
                && observation.OccurredAt < oldest)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    observation => observation.DeliveryStatus, ObservationDeliveryStatus.Abandoned),
                cancellationToken);

        var target = await database.CallbackTargets.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return;
        }

        var due = await database.Observations
            .Include(observation => observation.Session)
            .Where(observation => observation.DeliveryStatus == ObservationDeliveryStatus.Pending
                && observation.NextAttemptAt <= now)
            .OrderBy(observation => observation.OccurredAt)
            .Take(settings.DeliveryBatchSize)
            .ToListAsync(cancellationToken);
        var (sent, batch) = BuildBatch(due, settings.DeliveryBatchSize);
        if (sent.Count > 0)
        {
            using var client = httpClients.CreateClient(HttpClientName);
            var (delivered, statusCode) = await SendAsync(client, target, batch, cancellationToken);
            var completedAt = clock.GetUtcNow();
            foreach (var observation in sent)
            {
                if (delivered)
                {
                    observation.MarkDelivered(completedAt, statusCode!.Value);
                }
                else
                {
                    observation.MarkFailed(completedAt, statusCode, settings.MaxDeliveryAttempts, settings.RetryDelay);
                }
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Arranges the due observations as one callback body, applying each session's telemetry faults.</summary>
    private (List<ProviderObservation> Sent, List<ObservationResponse> Batch) BuildBatch(
        List<ProviderObservation> due, int maximumSize)
    {
        var sent = new List<ProviderObservation>();
        var batch = new List<ObservationResponse>();
        foreach (var session in due.GroupBy(observation => observation.Session!.ExternalSessionId))
        {
            var fault = faults.Find(session.Key);
            // The due rows are loaded oldest first and grouping keeps that order.
            var observations = session.ToList();
            if (fault is { Interrupt: true })
            {
                observations.ForEach(observation => observation.Suppress());
                continue;
            }

            if (fault is { DeliverOutOfOrder: true })
            {
                // Hold a lone reading until a newer one exists, so the newer one can overtake it.
                if (observations.Count < 2 && !observations.Any(observation => observation.IsFinalEnergy))
                {
                    continue;
                }

                observations.Reverse();
            }

            int copies = fault is { Duplicate: true } ? 2 : 1;
            foreach (var observation in observations)
            {
                if (batch.Count > 0 && batch.Count + copies > maximumSize)
                {
                    break;
                }

                sent.Add(observation);
                batch.AddRange(Enumerable.Repeat(observation.ToResponse(), copies));
            }
        }

        return (sent, batch);
    }

    private async Task<(bool Delivered, int? StatusCode)> SendAsync(
        HttpClient client, CallbackTarget target, List<ObservationResponse> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
            {
                Content = JsonContent.Create(new ObservationBatchPayload(batch), options: json.Value.SerializerOptions)
            };
            request.Headers.Add(ApiKeyHeader, target.ApiKey);
            using var response = await client.SendAsync(request, cancellationToken);
            int statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Delivering {Count} observations failed with status {StatusCode}.",
                    batch.Count,
                    statusCode);
            }

            return (response.IsSuccessStatusCode, statusCode);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Delivering {Count} observations failed; the target was not reached.",
                batch.Count);
            return (false, null);
        }
    }
}

/// <summary>The callback body: observations in the order they are reported.</summary>
internal sealed record ObservationBatchPayload(IReadOnlyList<ObservationResponse> Observations);
