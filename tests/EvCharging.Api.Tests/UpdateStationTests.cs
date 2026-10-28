using System.Collections.Concurrent;
using EvCharging.Api.Features.Stations;
using EvCharging.Contracts.Events.Stations;
using EvCharging.Testing;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Wolverine;
using Xunit;

namespace EvCharging.Api.Tests;

public sealed class UpdateStationTests(PlatformDatabaseFixture fixture) :
    IClassFixture<PlatformDatabaseFixture>,
    IAsyncLifetime
{
    private static readonly ConcurrentQueue<(Guid StationId, string? GroupId)> Published = [];
    private ApiHandlerHost? _api;

    public async Task InitializeAsync() =>
        _api = await ApiHandlerHost.StartAsync<UpdateStationCommandHandler>(
            fixture,
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            services =>
            {
                services.AddAuthorization();
                services.AddScoped<IAuthorizationHandler, StationAssignmentAuthorizationHandler>();
            },
            options =>
            {
                options.Discovery.IncludeType(typeof(StationChangedCapture));
                options.PublishMessage<StationChangedEvent>().ToLocalQueue("station-changed-test");
            });

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }

    [Fact]
    public async Task Update_AssignedOperator_PublishesStationChangedInTheEditTransactionGroup()
    {
        var (station, identity) = await fixture.SeedAssignedStationAsync();
        var request = new UpdateStationRequest(station.Version, "Renamed", station.Address, station.AccessInstructions);

        var result = await _api!.InvokeAsync<Result<StationEditorResponse>>(
            identity, new UpdateStationCommand(station.PublicId, request));

        Assert.True(result.IsSuccess);
        await WaitForPublishedAsync(station.Id);
        var published = Assert.Single(Published, value => value.StationId == station.Id);
        Assert.Equal(station.Id.ToString(), published.GroupId);
    }

    [Fact]
    public async Task Update_StaleVersion_PublishesNothing()
    {
        var (station, identity) = await fixture.SeedAssignedStationAsync();
        var request = new UpdateStationRequest(station.Version, "First", station.Address, station.AccessInstructions);
        Assert.True((await _api!.InvokeAsync<Result<StationEditorResponse>>(
            identity, new UpdateStationCommand(station.PublicId, request))).IsSuccess);
        await WaitForPublishedAsync(station.Id);

        var stale = await _api.InvokeAsync<Result<StationEditorResponse>>(
            identity,
            new UpdateStationCommand(station.PublicId, request with { Name = "Second" }));

        Assert.True(stale.IsFailed);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.Single(Published, value => value.StationId == station.Id);
    }

    private static Task WaitForPublishedAsync(Guid stationId) => Polling.WaitUntilAsync(
        () => Task.FromResult(Published.Any(value => value.StationId == stationId)),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMilliseconds(20));

    public static class StationChangedCapture
    {
        public static void Handle(StationChangedEvent message, Envelope envelope) =>
            Published.Enqueue((message.StationId, envelope.GroupId));
    }
}
