using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EvCharging.ProviderContracts.Tests;

public sealed class SynchronousProviderTests(ProviderFixture fixture) : IClassFixture<ProviderFixture>
{
    [Fact]
    public async Task Start_ReplayAndLookups_ReturnSameCommandAndSession()
    {
        var request = await ConfigureAsync();
        var reference = Guid.NewGuid();
        var result = await StartAsync(reference, request);
        Assert.Equal(reference, result.Reference);
        Assert.Equal("applied", result.Outcome);
        Assert.NotNull(result.SessionId);
        Assert.NotNull(result.AppliedAt);
        Assert.Null(result.Rejection);
        Assert.Equal(result, await StartAsync(reference, request));
        Assert.Equal(result, await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/{result.Id}"));
        Assert.Equal(
            result,
            await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/by-reference/{reference}"));

        var session = await fixture.Charging.GetFromJsonAsync<SessionResult>($"/sessions/{result.SessionId}");
        Assert.NotNull(session);
        Assert.Equal(result.SessionId, session.Id);
        Assert.Equal(request.SessionReference, session.Reference);
        Assert.Equal(request.StationId, session.StationId);
        Assert.Equal(request.ChargerId, session.ChargerId);
        Assert.Equal(request.ConnectorId, session.ConnectorId);
        Assert.Equal(result.AppliedAt, session.StartedAt);
        Assert.Equal(22m, session.PowerKw);
        Assert.Equal("charging", session.State);
        Assert.Equal(
            session,
            await fixture.Charging.GetFromJsonAsync<SessionResult>(
                $"/sessions/by-reference/{request.SessionReference}"));
        using var missing = await fixture.Charging.GetAsync($"/commands/by-reference/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Start_ConcurrentIdenticalRequests_ApplyOneCommand()
    {
        var request = await ConfigureAsync();
        var reference = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => StartAsync(reference, request)));
        Assert.All(results, result => Assert.Equal("applied", result.Outcome));
        Assert.Single(results.Distinct());
    }

    [Fact]
    public async Task Start_CompetingCommands_ApplyOnlyOneAndRetainRejections()
    {
        var request = await ConfigureAsync();
        var requests = Enumerable.Range(0, 8)
            .Select(_ => (Reference: Guid.NewGuid(), Request: request with { SessionReference = Guid.NewGuid() }))
            .ToArray();
        var statuses = await Task.WhenAll(requests.Select(async attempt =>
        {
            using var response = await SendStartAsync(attempt.Reference, attempt.Request);
            return response.StatusCode;
        }));
        Assert.Single(statuses, status => status == HttpStatusCode.OK);
        foreach (var (status, attempt) in statuses.Zip(requests))
        {
            if (status == HttpStatusCode.OK)
            {
                Assert.Equal("applied", (await StartAsync(attempt.Reference, attempt.Request)).Outcome);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, status);
                var rejected = await RejectStartAsync(attempt.Reference, attempt.Request);
                Assert.Equal("connector_unavailable", rejected.Code);
                using var session = await fixture.Charging
                    .GetAsync($"/sessions/by-reference/{attempt.Request.SessionReference}");
                Assert.Equal(HttpStatusCode.NotFound, session.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Start_ReusedKeyWithDifferentPayload_ReturnConflictAndPreserveOriginal()
    {
        var request = await ConfigureAsync();
        var reference = Guid.NewGuid();
        var original = await StartAsync(reference, request);
        using var conflict = await SendStartAsync(reference, request with { SessionReference = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency_conflict", problem.GetProperty("code").GetString());
        Assert.Equal(original, await StartAsync(reference, request));
    }

    [Fact]
    public async Task Start_DisabledConnector_ReturnReplayableRejection()
    {
        var request = await ConfigureAsync(enabled: false);
        var reference = Guid.NewGuid();
        var rejected = await RejectStartAsync(reference, request);
        Assert.Equal("connector_unavailable", rejected.Code);
        Assert.Equal(rejected, await RejectStartAsync(reference, request));
        var command = await fixture.Charging.GetFromJsonAsync<CommandResult>($"/commands/{rejected.CommandId}");
        Assert.NotNull(command);
        Assert.Equal(rejected.CommandId, command.Id);
        Assert.Equal(reference, command.Reference);
        Assert.Equal("rejected", command.Outcome);
        Assert.Equal(rejected.Code, command.Rejection);
        Assert.Null(command.SessionId);
        Assert.Null(command.AppliedAt);
    }

    [Fact]
    public async Task Start_ReusedSessionReference_RejectAndLeaveOtherConnectorAvailable()
    {
        var first = await ConfigureAsync();
        var second = await ConfigureAsync();
        Assert.Equal("applied", (await StartAsync(Guid.NewGuid(), first)).Outcome);
        var rejected = await RejectStartAsync(
            Guid.NewGuid(),
            second with { SessionReference = first.SessionReference });
        Assert.Equal("session_reference_conflict", rejected.Code);
        var connector = await fixture.Charging.GetFromJsonAsync<EquipmentResult>(EquipmentPath(second));
        Assert.NotNull(connector);
        Assert.False(connector.Occupied);
        Assert.Equal("applied", (await StartAsync(Guid.NewGuid(), second)).Outcome);
    }

    [Fact]
    public async Task Start_AnonymousCall_RejectWithoutChangingEquipment()
    {
        var request = await ConfigureAsync();
        using var anonymous = new HttpClient { BaseAddress = fixture.Charging.BaseAddress };
        using var unauthorized = await anonymous.PostAsJsonAsync("/commands/start", request);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var connector = await fixture.Charging.GetFromJsonAsync<EquipmentResult>(EquipmentPath(request));
        Assert.NotNull(connector);
        Assert.True(connector.Enabled);
        Assert.False(connector.Occupied);
    }

    [Fact]
    public async Task Write_InvalidInput_RejectWithoutChangingEquipment()
    {
        var request = await ConfigureAsync();
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
        var connector = await fixture.Charging.GetFromJsonAsync<EquipmentResult>(EquipmentPath(request));
        Assert.NotNull(connector);
        Assert.True(connector.Enabled);
        Assert.Equal(22m, connector.PowerKw);
        Assert.False(connector.Occupied);
    }

    private async Task<StartRequest> ConfigureAsync(bool enabled = true)
    {
        var request = new StartRequest(Guid.NewGuid().ToString(), "charger-1", "connector-1", Guid.NewGuid());
        await fixture.PrepareConnectorAsync(request.StationId, request.ChargerId, request.ConnectorId, enabled);
        return request;
    }

    private async Task<CommandResult> StartAsync(Guid reference, StartRequest request)
    {
        using var response = await SendStartAsync(reference, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<CommandResult>()
            ?? throw new InvalidOperationException("Expected a command response.");
    }

    private async Task<StartProblem> RejectStartAsync(Guid reference, StartRequest request)
    {
        using var response = await SendStartAsync(reference, request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<StartProblem>();
        Assert.NotNull(problem);
        Assert.Equal((int)response.StatusCode, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.NotEqual(Guid.Empty, problem.CommandId);
        Assert.Equal(reference, problem.Reference);
        return problem;
    }

    private async Task<HttpResponseMessage> SendStartAsync(Guid reference, StartRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/commands/start")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", reference.ToString());
        return await fixture.Charging.SendAsync(message);
    }

    private static string EquipmentPath(StartRequest request) =>
        $"/stations/{request.StationId}/chargers/{request.ChargerId}/connectors/{request.ConnectorId}";

    private sealed record StartRequest(string StationId, string ChargerId, string ConnectorId, Guid SessionReference);
    private sealed record EquipmentResult(decimal PowerKw, bool Enabled, bool Occupied);
    private sealed record StartProblem(
        int Status,
        string Title,
        string Detail,
        string Code,
        Guid CommandId,
        Guid Reference);
    private sealed record CommandResult(
        Guid Id,
        Guid Reference,
        string Outcome,
        Guid? SessionId,
        DateTimeOffset? AppliedAt,
        string? Rejection);
    private sealed record SessionResult(
        Guid Id,
        Guid Reference,
        string StationId,
        string ChargerId,
        string ConnectorId,
        DateTimeOffset StartedAt,
        decimal PowerKw,
        string State);
}
