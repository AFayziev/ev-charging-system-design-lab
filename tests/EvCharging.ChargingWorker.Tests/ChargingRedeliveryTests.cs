using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Testing;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Xunit;

namespace EvCharging.ChargingWorker.Tests;

// The worker's own failure policies and a handler resolved by Wolverine, so a failed save is retried as in
// production. The class keeps its own database: a second host on one message store would take the other's messages.
public sealed class ChargingRedeliveryTests(PlatformDatabaseFixture fixture) : IClassFixture<PlatformDatabaseFixture>
{
    private const string IntegrationKey = ChargingCommandTests.IntegrationKey;

    [Fact]
    public async Task Start_OutcomeSaveFailsTransiently_WolverineRedeliversWithoutASecondEffect()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var operation = await StartOperationSeed.CreateAsync(fixture, clock.GetUtcNow(), IntegrationKey);
        using var provider = new ChargingCommandTests.ProviderTransport(
            (_, _) => Task.FromResult(ChargingCommandTests.AppliedResponse(operation.PublicId, clock.GetUtcNow())));
        var failure = new TransientOutcomeSaveFailure();
        using var host = await StartWorkerAsync(clock, provider, failure);

        await host.MessageBus().SendAsync(new StartChargingMessage(operation.Id));
        var store = host.Services.GetRequiredService<IMessageStore>();
        await WaitForAsync(
            async () => failure.Thrown == 1 && (await store.Admin.AllIncomingAsync())
                .All(value => value.Status == EnvelopeStatus.Handled));

        // The one envelope was redelivered once and handled; the second delivery found the effect already made.
        var delivered = Assert.Single(await store.Admin.AllIncomingAsync());
        Assert.Equal(1, delivered.Attempts);
        Assert.Equal(1, provider.Calls);
        await using var database = fixture.CreateContext();
        var saved = await database.Operations.AsNoTracking().SingleAsync(value => value.Id == operation.Id);
        // The failed save rolled back, so the outcome is still open for the status checks to settle.
        Assert.Equal(CommandOutcome.Pending, saved.Outcome);
        Assert.Equal(1, saved.AuthorizedAttemptCount);
        Assert.Equal(1, await database.Attempts.CountAsync(value => value.OperationId == operation.Id));
    }

    private async Task<IHost> StartWorkerAsync(
        TimeProvider clock, ChargingCommandTests.ProviderTransport provider, TransientOutcomeSaveFailure failure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"{ChargingProviderIntegrationOptions.SectionName}:{IntegrationKey}:Client"] =
            ChargingProviderClientType.WattHarbor.ToString();
        builder.Services.AddOptions<ChargingProviderIntegrationOptions>(IntegrationKey)
            .Configure(value => value.Client = ChargingProviderClientType.WattHarbor);
        builder.Services.AddHttpClient(
            ChargingProviderClientFactory.HttpClientName(IntegrationKey),
            client => client.BaseAddress = new("https://provider.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => provider);
        builder.Services.AddTransient<ChargingProviderClientFactory>();
        builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton(Options.Create(ChargingCommandTests.TestExecutionOptions()));
        builder.Services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(fixture.GetDataSource("charging_messaging"))
                .AddInterceptors(failure));
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType<StartChargingMessageHandler>();
            options.UseEntityFrameworkCoreTransactions();
            options.PersistMessagesWithPostgresql(
                fixture.GetConnectionString("platform_migrator"), "charging_messages");
            options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            options.CodeGeneration.AlwaysUseServiceLocationFor<ChargingProviderClientFactory>();
            options.Policies.UseDurableLocalQueues();
            options.AddFailurePolicies(new() { TransientDatabaseRetryDelays = [TimeSpan.FromMilliseconds(200)] });
        });
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    // Fails the first outcome save as a lost connection does, after the provider call was made.
    private sealed class TransientOutcomeSaveFailure : SaveChangesInterceptor
    {
        private int _thrown;
        public int Thrown => _thrown;

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Operation>()
                .Any(value => value.Entity.Outcome == CommandOutcome.Confirmed) == true
                && Interlocked.Increment(ref _thrown) == 1)
            {
                throw new NpgsqlException("Connection lost.", new IOException());
            }

            return ValueTask.FromResult(result);
        }
    }
}
