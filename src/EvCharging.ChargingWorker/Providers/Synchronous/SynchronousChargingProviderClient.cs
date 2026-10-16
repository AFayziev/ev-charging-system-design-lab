using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentResults;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace EvCharging.ChargingWorker.Providers.Synchronous;

internal sealed class SynchronousChargingProviderClient(
    HttpClient client,
    ILogger<SynchronousChargingProviderClient> logger)
{
    private const string ConnectorUnavailable = "connector_unavailable";
    private const string SessionReferenceConflict = "session_reference_conflict";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public async Task<Result<ProviderCommandResult>> StartAsync(
        Guid operationId,
        Guid sessionId,
        ProviderEquipmentAddress equipment,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Provider"] = "synchronous",
            ["OperationId"] = operationId,
            ["SessionId"] = sessionId
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "commands/start")
        {
            Content = JsonContent.Create(new StartRequest(
                equipment.StationId, equipment.ChargerId, equipment.ConnectorId, sessionId))
        };
        request.Headers.Add("Idempotency-Key", operationId.ToString());
        return await SendAsync(request, (response, body) => ReadStart(response, body, operationId), cancellationToken);
    }

    public Task<Result<ProviderCommandResult?>> GetCommandAsync(Guid id, CancellationToken cancellationToken) =>
        GetCommandAsync($"commands/{id}", command => command.Id == id, cancellationToken);

    public Task<Result<ProviderCommandResult?>> GetCommandByReferenceAsync(
        Guid reference, CancellationToken cancellationToken) =>
        GetCommandAsync(
            $"commands/by-reference/{reference}", command => command.Reference == reference, cancellationToken);

    public Task<Result<ProviderSessionResult?>> GetSessionAsync(Guid id, CancellationToken cancellationToken) =>
        GetSessionAsync($"sessions/{id}", session => session.Id == id, cancellationToken);

    public Task<Result<ProviderSessionResult?>> GetSessionByReferenceAsync(
        Guid reference, CancellationToken cancellationToken) =>
        GetSessionAsync(
            $"sessions/by-reference/{reference}", session => session.Reference == reference, cancellationToken);

    private static Result<ProviderCommandResult> ReadStart(
        HttpResponseMessage response, string body, Guid reference)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var command = Deserialize<ProviderCommandResult>(body);
            bool matches = IsValid(command) && command.Outcome == ProviderCommandOutcome.Applied
                && command.Reference == reference;
            return matches
                ? Result.Ok(command with { AppliedAt = command.AppliedAt?.ToUniversalTime() })
                : Result.Fail<ProviderCommandResult>(new ProviderResponseError(response.StatusCode, body));
        }

        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            var problem = Deserialize<StartProblem>(body);
            if (response.StatusCode == HttpStatusCode.Conflict && problem.Status == (int)HttpStatusCode.Conflict
                && IsRejection(problem.Code)
                && problem.CommandId is { } commandId && commandId != Guid.Empty && problem.Reference == reference)
            {
                return Result.Fail<ProviderCommandResult>(new ProviderRejectedError(
                    commandId, reference, problem.Code));
            }

            return Result.Fail<ProviderCommandResult>(new ProviderResponseError(
                response.StatusCode, body, problem.Code));
        }

        return Result.Fail<ProviderCommandResult>(new ProviderResponseError(response.StatusCode, body));
    }

    private async Task<Result<ProviderCommandResult?>> GetCommandAsync(
        string path, Func<ProviderCommandResult, bool> matches, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<ProviderCommandResult?>(
            request,
            (response, body) =>
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Result.Ok<ProviderCommandResult?>(null);
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var command = Deserialize<ProviderCommandResult>(body);
                    if (IsValid(command) && matches(command))
                    {
                        var normalized = command with
                        {
                            AppliedAt = command.AppliedAt?.ToUniversalTime()
                        };
                        return Result.Ok<ProviderCommandResult?>(normalized);
                    }
                }

                return Result.Fail<ProviderCommandResult?>(new ProviderResponseError(response.StatusCode, body));
            },
            cancellationToken);
    }

    private async Task<Result<ProviderSessionResult?>> GetSessionAsync(
        string path, Func<SessionResponse, bool> matches, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<ProviderSessionResult?>(
            request,
            (response, body) =>
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return Result.Ok<ProviderSessionResult?>(null);
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var session = Deserialize<SessionResponse>(body);
                    if (session.Id != Guid.Empty && session.Reference != Guid.Empty && session.StartedAt != default
                        && session.PowerKw > 0 && !string.IsNullOrWhiteSpace(session.StationId)
                        && !string.IsNullOrWhiteSpace(session.ChargerId)
                        && !string.IsNullOrWhiteSpace(session.ConnectorId)
                        && matches(session))
                    {
                        return Result.Ok<ProviderSessionResult?>(new ProviderSessionResult(
                            session.Id,
                            session.Reference,
                            new ProviderEquipmentAddress(session.StationId, session.ChargerId, session.ConnectorId),
                            session.StartedAt.ToUniversalTime(),
                            session.PowerKw,
                            session.State));
                    }
                }

                return Result.Fail<ProviderSessionResult?>(new ProviderResponseError(response.StatusCode, body));
            },
            cancellationToken);
    }

    private async Task<Result<T>> SendAsync<T>(
        HttpRequestMessage request,
        Func<HttpResponseMessage, string, Result<T>> read,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.OriginalString;
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                var result = read(response, body);
                LogFailure(result, response.StatusCode, path);
                return result;
            }
            catch (JsonException)
            {
                logger.LogError(
                    "Provider returned an invalid response for {ProviderPath}, HTTP {StatusCode}",
                    path,
                    (int)response.StatusCode);
                return Result.Fail<T>(new ProviderResponseError(response.StatusCode, body));
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Provider request timed out for {ProviderPath}", path);
            return Result.Fail<T>(new ProviderTransportError(timedOut: true).CausedBy(exception));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TimeoutRejectedException
                or BrokenCircuitException or RateLimiterRejectedException)
        {
            logger.LogWarning(exception, "Provider request failed for {ProviderPath}", path);
            return Result.Fail<T>(new ProviderTransportError(
                timedOut: exception is TimeoutRejectedException)
                .CausedBy(exception));
        }
    }

    private void LogFailure<T>(Result<T> result, HttpStatusCode status, string? path)
    {
        if (result.HasError<ProviderRejectedError>(out var rejections))
        {
            var rejection = rejections.First();
            logger.LogInformation(
                "Provider rejected command {ProviderCommandId} with code {RejectionCode}",
                rejection.CommandId,
                rejection.Code);
        }
        else if (result.HasError<ProviderResponseError>(out var errors))
        {
            var level = (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                ? LogLevel.Warning : LogLevel.Error;
            logger.Log(
                level,
                "Provider response for {ProviderPath}, HTTP {StatusCode}, code {ProviderCode}, has no matching outcome",
                path,
                (int)status,
                errors.First().Code);
        }
    }

    private static T Deserialize<T>(string body) => JsonSerializer.Deserialize<T>(body, JsonOptions)
        ?? throw new JsonException("The provider response is empty.");

    private static bool IsRejection([NotNullWhen(true)] string? code) =>
        code is ConnectorUnavailable or SessionReferenceConflict;

    private static bool IsValid(ProviderCommandResult command) =>
        command.Id != Guid.Empty && command.Reference != Guid.Empty && (command.Outcome switch
        {
            ProviderCommandOutcome.Applied => command.SessionId is { } sessionId && sessionId != Guid.Empty
                && command.AppliedAt is { } appliedAt && appliedAt != default && command.Rejection is null,
            ProviderCommandOutcome.Rejected => command.SessionId is null && command.AppliedAt is null
                && IsRejection(command.Rejection),
            _ => false
        });

    private sealed record StartRequest(string StationId, string ChargerId, string ConnectorId, Guid SessionReference);
    private sealed record StartProblem(
        int? Status = null,
        string? Code = null,
        Guid? CommandId = null,
        Guid? Reference = null);
    private sealed record SessionResponse(
        Guid Id,
        Guid Reference,
        string StationId,
        string ChargerId,
        string ConnectorId,
        DateTimeOffset StartedAt,
        decimal PowerKw,
        ProviderSessionState State);
}
