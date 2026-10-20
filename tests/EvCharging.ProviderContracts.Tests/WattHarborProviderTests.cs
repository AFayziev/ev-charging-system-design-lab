using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EvCharging.ChargingWorker.Providers;
using EvCharging.ChargingWorker.Providers.WattHarbor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EvCharging.ProviderContracts.Tests;

public sealed class WattHarborProviderTests(WattHarborFixture fixture) : IClassFixture<WattHarborFixture>
{
    [Fact]
    public async Task Start_ReplayAndLookups_ReturnSameCommandAndSession()
    {
        var request = await ConfigureAsync();
        var externalOperationId = Guid.NewGuid();
        var result = await StartAsync(externalOperationId, request);
        Assert.Equal(externalOperationId, result.ExternalOperationId);
        Assert.Equal("start", result.Kind);
        Assert.Equal("applied", result.Outcome);
        Assert.NotNull(result.SessionId);
        Assert.NotNull(result.AppliedAt);
        Assert.Null(result.Rejection);
        Assert.Equal(result, await StartAsync(externalOperationId, request));
        Assert.Equal(result, await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/{result.Id}"));
        Assert.Equal(
            result,
            await fixture.Charging.GetFromJsonAsync<CommandResult>(
                $"/commands/by-external-operation/{externalOperationId}"));

        var session = await fixture.Charging.GetFromJsonAsync<SessionResult>($"/sessions/{result.SessionId}");
        Assert.NotNull(session);
        Assert.Equal(result.SessionId, session.Id);
        Assert.Equal(request.ExternalSessionId, session.ExternalSessionId);
        Assert.Equal(request.StationId, session.StationId);
        Assert.Equal(request.ChargerId, session.ChargerId);
        Assert.Equal(request.ConnectorId, session.ConnectorId);
        Assert.Equal(result.AppliedAt, session.StartedAt);
        Assert.Equal(22m, session.PowerKw);
        Assert.Equal("charging", session.State);
        Assert.Null(session.EndedAt);
        Assert.Equal(
            session,
            await fixture.Charging.GetFromJsonAsync<SessionResult>(
                $"/sessions/by-external-session/{request.ExternalSessionId}"));
        using var missing = await fixture.Charging.GetAsync($"/commands/by-external-operation/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Start_ConcurrentIdenticalRequests_ApplyOneCommand()
    {
        var request = await ConfigureAsync();
        var externalOperationId = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => StartAsync(externalOperationId, request)));
        Assert.All(results, result => Assert.Equal("applied", result.Outcome));
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task Start_CompetingCommands_ApplyOnlyOneAndRetainRejections()
    {
        var request = await ConfigureAsync();
        var requests = Enumerable.Range(0, 8)
            .Select(_ => (
                ExternalOperationId: Guid.NewGuid(), Request: request with { ExternalSessionId = Guid.NewGuid() }))
            .ToArray();
        var statuses = await Task.WhenAll(requests.Select(async attempt =>
        {
            using var response = await SendStartAsync(attempt.ExternalOperationId, attempt.Request);
            return response.StatusCode;
        }));
        Assert.Single(statuses, status => status == HttpStatusCode.OK);
        foreach (var (status, attempt) in statuses.Zip(requests))
        {
            if (status == HttpStatusCode.OK)
            {
                Assert.Equal("applied", (await StartAsync(attempt.ExternalOperationId, attempt.Request)).Outcome);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, status);
                var rejected = await RejectStartAsync(attempt.ExternalOperationId, attempt.Request);
                Assert.Equal("connector_unavailable", rejected.Code);
                using var session = await fixture.Charging
                    .GetAsync($"/sessions/by-external-session/{attempt.Request.ExternalSessionId}");
                Assert.Equal(HttpStatusCode.NotFound, session.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Start_ReusedKeyWithDifferentPayload_ReturnConflictAndPreserveOriginal()
    {
        var request = await ConfigureAsync();
        var externalOperationId = Guid.NewGuid();
        var original = await StartAsync(externalOperationId, request);
        using var conflict = await SendStartAsync(
            externalOperationId, request with { ExternalSessionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency_conflict", problem.GetProperty("code").GetString());
        Assert.Equal(original, await StartAsync(externalOperationId, request));
    }

    [Fact]
    public async Task Start_DisabledConnector_ReturnReplayableRejection()
    {
        var request = await ConfigureAsync(enabled: false);
        var externalOperationId = Guid.NewGuid();
        var rejected = await RejectStartAsync(externalOperationId, request);
        Assert.Equal("connector_unavailable", rejected.Code);
        Assert.Equal(rejected, await RejectStartAsync(externalOperationId, request));
        var command = await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/{rejected.CommandId}");
        Assert.NotNull(command);
        Assert.Equal(rejected.CommandId, command.Id);
        Assert.Equal(externalOperationId, command.ExternalOperationId);
        Assert.Equal("rejected", command.Outcome);
        Assert.Equal(rejected.Code, command.Rejection);
        Assert.Null(command.SessionId);
        Assert.Null(command.AppliedAt);
    }

    [Fact]
    public async Task Start_ReusedExternalSessionId_RejectAndLeaveOtherConnectorAvailable()
    {
        var first = await ConfigureAsync();
        var second = await ConfigureAsync();
        Assert.Equal("applied", (await StartAsync(Guid.NewGuid(), first)).Outcome);
        var rejected = await RejectStartAsync(
            Guid.NewGuid(),
            second with { ExternalSessionId = first.ExternalSessionId });
        Assert.Equal("external_session_id_conflict", rejected.Code);
        var connector = await fixture.Charging.GetFromJsonAsync<EquipmentResult>(EquipmentPath(second));
        Assert.NotNull(connector);
        Assert.False(connector.Occupied);
        Assert.Equal("applied", (await StartAsync(Guid.NewGuid(), second)).Outcome);
    }

    [Fact]
    public async Task Start_AnonymousCall_RequiresAuthentication()
    {
        var request = NewStartRequest();
        using var anonymous = new HttpClient { BaseAddress = fixture.Charging.BaseAddress };
        var externalOperationId = Guid.NewGuid();
        using var start = new HttpRequestMessage(HttpMethod.Post, "/commands/start")
        {
            Content = JsonContent.Create(request)
        };
        start.Headers.Add("Idempotency-Key", externalOperationId.ToString());
        using var unauthorized = await anonymous.SendAsync(start);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Start_InvalidInput_ReturnsBadRequest()
    {
        var request = NewStartRequest();
        using var missingHeader = await fixture.Charging.PostAsJsonAsync("/commands/start", request);
        Assert.Equal(HttpStatusCode.BadRequest, missingHeader.StatusCode);
        using var malformedHeaderRequest = new HttpRequestMessage(HttpMethod.Post, "/commands/start")
        {
            Content = JsonContent.Create(request)
        };
        malformedHeaderRequest.Headers.Add("Idempotency-Key", "not-a-uuid");
        using var malformedHeader = await fixture.Charging.SendAsync(malformedHeaderRequest);
        Assert.Equal(HttpStatusCode.BadRequest, malformedHeader.StatusCode);
        using var invalid = await SendStartAsync(Guid.Empty, request);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var malformed = await SendStartAsync(Guid.NewGuid(), request with { StationId = "" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task Stop_ReplayAndLookups_EndSessionAndReleaseConnector()
    {
        var request = await ConfigureAsync();
        var start = await StartAsync(Guid.NewGuid(), request);
        var externalOperationId = Guid.NewGuid();
        var stop = new StopRequest(request.ExternalSessionId);

        var result = await StopAsync(externalOperationId, stop);

        Assert.Equal(externalOperationId, result.ExternalOperationId);
        Assert.Equal("stop", result.Kind);
        Assert.Equal("applied", result.Outcome);
        Assert.Equal(start.SessionId, result.SessionId);
        Assert.NotNull(result.AppliedAt);
        Assert.Null(result.Rejection);
        Assert.Equal(result, await StopAsync(externalOperationId, stop));
        Assert.Equal(result, await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/{result.Id}"));
        Assert.Equal(
            result,
            await fixture.Charging.GetFromJsonAsync<CommandResult>(
                $"/commands/by-external-operation/{externalOperationId}"));
        Assert.Equal("start", (await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/{start.Id}"))?.Kind);

        var session = await fixture.Charging.GetFromJsonAsync<SessionResult>($"/sessions/{start.SessionId}");
        Assert.NotNull(session);
        Assert.Equal("stopped", session.State);
        Assert.Equal(result.AppliedAt, session.EndedAt);
        Assert.Equal(start.AppliedAt, session.StartedAt);
        var connector = await fixture.Charging.GetFromJsonAsync<EquipmentResult>(EquipmentPath(request));
        Assert.False(connector?.Occupied);
        var next = await StartAsync(Guid.NewGuid(), request with { ExternalSessionId = Guid.NewGuid() });
        Assert.Equal("applied", next.Outcome);
    }

    [Fact]
    public async Task Stop_AlreadyStoppedSession_IsAppliedWithoutChangingTheEnd()
    {
        var request = await ConfigureAsync();
        var start = await StartAsync(Guid.NewGuid(), request);
        var stop = new StopRequest(request.ExternalSessionId);
        var first = await StopAsync(Guid.NewGuid(), stop);
        var second = await StopAsync(Guid.NewGuid(), stop);

        Assert.Equal("applied", second.Outcome);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.NotEqual(first.Id, second.Id);
        var session = await fixture.Charging.GetFromJsonAsync<SessionResult>($"/sessions/{start.SessionId}");
        Assert.Equal(first.AppliedAt, session?.EndedAt);
    }

    [Fact]
    public async Task Stop_UnknownSession_ReturnsReplayableRejection()
    {
        var externalOperationId = Guid.NewGuid();
        var stop = new StopRequest(Guid.NewGuid());

        var rejected = await RejectStopAsync(externalOperationId, stop);

        Assert.Equal("session_not_found", rejected.Code);
        Assert.Equal(rejected, await RejectStopAsync(externalOperationId, stop));
        var command = await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/{rejected.CommandId}");
        Assert.NotNull(command);
        Assert.Equal("stop", command.Kind);
        Assert.Equal("rejected", command.Outcome);
        Assert.Equal(rejected.Code, command.Rejection);
        Assert.Null(command.SessionId);
        Assert.Null(command.AppliedAt);
    }

    [Fact]
    public async Task Stop_ConcurrentRequests_EndTheSessionOnce()
    {
        var request = await ConfigureAsync();
        await StartAsync(Guid.NewGuid(), request);
        var sameKey = Guid.NewGuid();
        var stop = new StopRequest(request.ExternalSessionId);

        var identical = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => StopAsync(sameKey, stop)));
        Assert.Single(identical.Distinct());

        var other = await ConfigureAsync();
        var otherStart = await StartAsync(Guid.NewGuid(), other);
        var competing = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => StopAsync(Guid.NewGuid(), new StopRequest(other.ExternalSessionId))));
        Assert.All(competing, result => Assert.Equal("applied", result.Outcome));
        var session = await fixture.Charging.GetFromJsonAsync<SessionResult>($"/sessions/{otherStart.SessionId}");
        Assert.Contains(session?.EndedAt, competing.Select(result => result.AppliedAt));
        Assert.False((await fixture.Charging.GetFromJsonAsync<EquipmentResult>(EquipmentPath(other)))?.Occupied);
    }

    [Fact]
    public async Task Stop_ReusedKey_ReturnsConflictAndPreservesOriginal()
    {
        var request = await ConfigureAsync();
        var startKey = Guid.NewGuid();
        await StartAsync(startKey, request);
        using var asStop = await SendStopAsync(startKey, new StopRequest(request.ExternalSessionId));
        Assert.Equal(HttpStatusCode.Conflict, asStop.StatusCode);
        Assert.Equal(
            "idempotency_conflict",
            (await asStop.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var stopKey = Guid.NewGuid();
        var original = await StopAsync(stopKey, new StopRequest(request.ExternalSessionId));
        using var otherSession = await SendStopAsync(stopKey, new StopRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, otherSession.StatusCode);
        using var asStart = await SendStartAsync(stopKey, request);
        Assert.Equal(HttpStatusCode.Conflict, asStart.StatusCode);
        Assert.Equal(original, await StopAsync(stopKey, new StopRequest(request.ExternalSessionId)));
        Assert.Equal("applied", (await StartAsync(startKey, request)).Outcome);
    }

    [Fact]
    public async Task StopClient_AppliedAndUnknownSession_MapToCommandEvidence()
    {
        var request = await ConfigureAsync();
        var client = new WattHarborChargingClient(fixture.Charging, NullLogger<WattHarborChargingClient>.Instance);
        var equipment = new ProviderEquipmentAddress(request.StationId, request.ChargerId, request.ConnectorId);
        var started = await client.StartAsync(Guid.NewGuid(), request.ExternalSessionId, equipment, default);
        Assert.Equal(ProviderCommandKind.Start, started.Value.Kind);
        var stopKey = Guid.NewGuid();

        var stopped = await client.StopAsync(stopKey, request.ExternalSessionId, default);

        Assert.True(stopped.IsSuccess);
        Assert.Equal(ProviderCommandKind.Stop, stopped.Value.Kind);
        Assert.Equal(ProviderCommandOutcome.Applied, stopped.Value.Outcome);
        Assert.Equal(started.Value.SessionId, stopped.Value.SessionId);
        Assert.Equal(stopped.Value, (await client.StopAsync(stopKey, request.ExternalSessionId, default)).Value);
        Assert.Equal(stopped.Value, (await client.GetCommandByExternalOperationIdAsync(stopKey, default)).Value);

        var unknownKey = Guid.NewGuid();
        var unknown = await client.StopAsync(unknownKey, Guid.NewGuid(), default);

        var rejection = Assert.IsType<ProviderRejectedError>(Assert.Single(unknown.Errors));
        Assert.Equal("session_not_found", rejection.Code);
        Assert.Equal(unknownKey, rejection.ExternalOperationId);
        var recorded = (await client.GetCommandByExternalOperationIdAsync(unknownKey, default)).Value;
        Assert.Equal(ProviderCommandOutcome.Rejected, recorded?.Outcome);
        Assert.Equal(rejection.CommandId, recorded?.Id);
    }

    private async Task<CommandResult> StopAsync(Guid externalOperationId, StopRequest request)
    {
        using var response = await SendStopAsync(externalOperationId, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<CommandResult>()
            ?? throw new InvalidOperationException("Expected a command response.");
    }

    private async Task<CommandProblem> RejectStopAsync(Guid externalOperationId, StopRequest request)
    {
        using var response = await SendStopAsync(externalOperationId, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<CommandProblem>();
        Assert.NotNull(problem);
        Assert.Equal("Charging stop rejected", problem.Title);
        Assert.NotEqual(Guid.Empty, problem.CommandId);
        Assert.Equal(externalOperationId, problem.ExternalOperationId);
        return problem;
    }

    private Task<HttpResponseMessage> SendStopAsync(Guid externalOperationId, StopRequest request) =>
        SendCommandAsync("/commands/stop", externalOperationId, request);

    private async Task<HttpResponseMessage> SendCommandAsync(string path, Guid externalOperationId, object request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", externalOperationId.ToString());
        return await fixture.Charging.SendAsync(message);
    }

    private async Task<StartRequest> ConfigureAsync(bool enabled = true)
    {
        var request = NewStartRequest();
        await fixture.PrepareConnectorAsync(request.StationId, request.ChargerId, request.ConnectorId, enabled);
        return request;
    }

    private async Task<CommandResult> StartAsync(Guid externalOperationId, StartRequest request)
    {
        using var response = await SendStartAsync(externalOperationId, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<CommandResult>()
            ?? throw new InvalidOperationException("Expected a command response.");
    }

    private async Task<CommandProblem> RejectStartAsync(Guid externalOperationId, StartRequest request)
    {
        using var response = await SendStartAsync(externalOperationId, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<CommandProblem>();
        Assert.NotNull(problem);
        Assert.Equal((int)response.StatusCode, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.NotEqual(Guid.Empty, problem.CommandId);
        Assert.Equal(externalOperationId, problem.ExternalOperationId);
        return problem;
    }

    private Task<HttpResponseMessage> SendStartAsync(Guid externalOperationId, StartRequest request) =>
        SendCommandAsync("/commands/start", externalOperationId, request);

    private static string EquipmentPath(StartRequest request) =>
        $"/stations/{request.StationId}/chargers/{request.ChargerId}/connectors/{request.ConnectorId}";

    private static StartRequest NewStartRequest() =>
        new(Guid.NewGuid().ToString(), "charger-1", "connector-1", Guid.NewGuid());

    private sealed record StartRequest(string StationId, string ChargerId, string ConnectorId, Guid ExternalSessionId);
    private sealed record StopRequest(Guid ExternalSessionId);
    private sealed record EquipmentResult(bool Occupied);
    private sealed record CommandProblem(
        int Status,
        string Title,
        string Detail,
        string Code,
        Guid CommandId,
        Guid ExternalOperationId);
    private sealed record CommandResult(
        Guid Id,
        Guid ExternalOperationId,
        string Kind,
        string Outcome,
        Guid? SessionId,
        DateTimeOffset? AppliedAt,
        string? Rejection);
    private sealed record SessionResult(
        Guid Id,
        Guid ExternalSessionId,
        string StationId,
        string ChargerId,
        string ConnectorId,
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        decimal PowerKw,
        string State);
}
