using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    private async Task<StartProblem> RejectStartAsync(Guid externalOperationId, StartRequest request)
    {
        using var response = await SendStartAsync(externalOperationId, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<StartProblem>();
        Assert.NotNull(problem);
        Assert.Equal((int)response.StatusCode, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.NotEqual(Guid.Empty, problem.CommandId);
        Assert.Equal(externalOperationId, problem.ExternalOperationId);
        return problem;
    }

    private async Task<HttpResponseMessage> SendStartAsync(Guid externalOperationId, StartRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/commands/start")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", externalOperationId.ToString());
        return await fixture.Charging.SendAsync(message);
    }

    private static string EquipmentPath(StartRequest request) =>
        $"/stations/{request.StationId}/chargers/{request.ChargerId}/connectors/{request.ConnectorId}";

    private static StartRequest NewStartRequest() =>
        new(Guid.NewGuid().ToString(), "charger-1", "connector-1", Guid.NewGuid());

    private sealed record StartRequest(string StationId, string ChargerId, string ConnectorId, Guid ExternalSessionId);
    private sealed record EquipmentResult(bool Occupied);
    private sealed record StartProblem(
        int Status,
        string Title,
        string Detail,
        string Code,
        Guid CommandId,
        Guid ExternalOperationId);
    private sealed record CommandResult(
        Guid Id,
        Guid ExternalOperationId,
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
        decimal PowerKw,
        string State);
}
