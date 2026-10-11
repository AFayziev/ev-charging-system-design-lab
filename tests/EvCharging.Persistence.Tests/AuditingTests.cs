using System.Diagnostics;
using EvCharging.Domain.Equipment;
using EvCharging.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace EvCharging.Persistence.Tests;

public sealed class AuditingTests(PlatformDatabaseFixture database) : IClassFixture<PlatformDatabaseFixture>
{
    [Fact]
    public async Task SaveStation_AddAndDelete_AuditsSelectedFields()
    {
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.Service("station-importer")), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(connection, writer);
        var station = new Station
        {
            Name = "Created station",
            Address = "Creation address",
            AccessInstructions = "Creation instructions",
            TimeZoneId = "UTC",
            Latitude = 42
        };
        context.Stations.Add(station);
        context.SaveChanges();

        Guid stationId = station.Id;
        var created = await context.AuditEntries.SingleAsync(audit => audit.ResourceId == stationId);
        Assert.Equal(AuditAction.Added, created.Action);
        Assert.Equal(AuditResourceType.Station, created.ResourceType);
        Assert.Equal("station-importer", created.ActorSubject);
        var after = created.Details.GetProperty("after");
        Assert.Equal(3, after.EnumerateObject().Count());
        Assert.Equal(station.Name, after.GetProperty("name").GetString());
        Assert.Equal(station.Address, after.GetProperty("address").GetString());
        Assert.Equal(station.AccessInstructions, after.GetProperty("accessInstructions").GetString());
        Assert.False(created.Details.TryGetProperty("before", out _));

        context.Stations.Remove(station);
        await context.SaveChangesAsync();
        Assert.False(await context.Stations.AnyAsync(candidate => candidate.Id == stationId));
        var deleted = await context.AuditEntries.SingleAsync(audit =>
            audit.ResourceId == stationId && audit.Action == AuditAction.Deleted);
        Assert.Equal(after.GetRawText(), deleted.Details.GetProperty("before").GetRawText());
        Assert.False(deleted.Details.TryGetProperty("after", out _));
        Assert.Equal(2, await context.AuditEntries.CountAsync(audit => audit.ResourceId == stationId));
    }

    [Fact]
    public async Task SaveStation_CreationAuditFailure_RollsBackAndRetryAuditsOnce()
    {
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.Service("station-importer")), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(connection, writer);
        var station = new Station
        {
            Name = $"Retried creation {Guid.NewGuid()}",
            Address = "Retry address",
            AccessInstructions = "Retry instructions",
            TimeZoneId = "UTC"
        };
        context.Stations.Add(station);
        await using var owner = database.CreateConnection("platform_migrator");
        await owner.OpenAsync();
        await WithAuditInsertDeniedAsync(
            owner,
            async () =>
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                Assert.Equal(EntityState.Added, context.Entry(station).State);
                Assert.False(await context.Stations.AnyAsync(candidate => candidate.Name == station.Name));
                Guid attemptedId = context.Entry(station).Property(candidate => candidate.Id).CurrentValue;
                Assert.False(await context.AuditEntries.AnyAsync(audit => audit.ResourceId == attemptedId));
            });

        await context.SaveChangesAsync();
        Assert.NotEqual(Guid.Empty, station.Id);
        Assert.Equal(1, await context.Stations.CountAsync(candidate => candidate.Name == station.Name));
        var audit = await context.AuditEntries.SingleAsync(audit => audit.ResourceId == station.Id);
        Assert.Equal(AuditAction.Added, audit.Action);
        Assert.Equal(station.Name, audit.Details.GetProperty("after").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SaveStation_CallerTransaction_AuditFailurePreservesEarlierSave()
    {
        Guid stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.Service("station-importer")), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(connection, writer);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        await using var transaction = await context.Database.BeginTransactionAsync();
        station.Name = "Earlier save";
        await context.SaveChangesAsync();

        station.Name = "Rejected save";
        await using var owner = database.CreateConnection("platform_migrator");
        await owner.OpenAsync();
        await WithAuditInsertDeniedAsync(
            owner,
            async () =>
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            });

        Assert.Same(transaction, context.Database.CurrentTransaction);
        await transaction.CommitAsync();
        await using var readerConnection = database.CreateConnection("platform_reader");
        await using var reader = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(readerConnection).Options);
        Assert.Equal(
            "Earlier save",
            await reader.Stations.Where(candidate => candidate.Id == stationId)
                .Select(candidate => candidate.Name)
                .SingleAsync());
        var audit = await reader.AuditEntries.SingleAsync(audit => audit.ResourceId == stationId);
        Assert.Equal("Earlier save", audit.Details.GetProperty("after").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SaveStation_DeleteAuditFailure_RollsBackAndRetryAuditsOnce()
    {
        Guid stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.Service("station-importer")), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(connection, writer);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        context.Stations.Remove(station);
        await using var owner = database.CreateConnection("platform_migrator");
        await owner.OpenAsync();
        await WithAuditInsertDeniedAsync(
            owner,
            async () =>
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                Assert.Equal(EntityState.Deleted, context.Entry(station).State);
                Assert.True(await context.Stations.AnyAsync(candidate => candidate.Id == stationId));
                Assert.False(await context.AuditEntries.AnyAsync(audit => audit.ResourceId == stationId));
            });

        await context.SaveChangesAsync();
        Assert.False(await context.Stations.AnyAsync(candidate => candidate.Id == stationId));
        var audit = await context.AuditEntries.SingleAsync(audit => audit.ResourceId == stationId);
        Assert.Equal(AuditAction.Deleted, audit.Action);
        Assert.Equal("Original station", audit.Details.GetProperty("before").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SaveStation_AuditInsertFailure_RollsBackAndRetryAuditsOnce()
    {
        Guid stationId = await SeedStationAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.User("https://identity.example", "operator")),
            clock);
        await using var writerConnection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(writerConnection, writer);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        station.Name = "Edited station";

        await using var ownerConnection = database.CreateConnection("platform_migrator");
        await ownerConnection.OpenAsync();
        await WithAuditInsertDeniedAsync(
            ownerConnection,
            async () =>
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                var postgres = Assert.IsType<PostgresException>(error.InnerException);
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgres.SqlState);
                await using var readerConnection = database.CreateConnection("platform_reader");
                await using var reader = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
                    .UseNpgsql(readerConnection).Options);
                Assert.Equal(
                    "Original station",
                    await reader.Stations.Where(station => station.Id == stationId)
                        .Select(station => station.Name)
                        .SingleAsync());
                Assert.Equal(0, await reader.AuditEntries.CountAsync(audit => audit.ResourceId == stationId));
            });

        using var trace = new Activity("station-edit").SetIdFormat(ActivityIdFormat.W3C).Start();
        await context.SaveChangesAsync();
        var audit = await context.AuditEntries.SingleAsync(audit => audit.ResourceId == stationId);
        Assert.Equal(AuditAction.Updated, audit.Action);
        Assert.Equal(AuditResourceType.Station, audit.ResourceType);
        Assert.Equal(AuditActorType.User, audit.ActorType);
        Assert.Equal("https://identity.example", audit.ActorIssuer);
        Assert.Equal("operator", audit.ActorSubject);
        Assert.Equal(clock.GetUtcNow(), audit.OccurredAt);
        Assert.Equal(trace.TraceId.ToString(), audit.TraceId);
        Assert.Equal(1, audit.DetailsVersion);
        var before = audit.Details.GetProperty("before");
        var after = audit.Details.GetProperty("after");
        Assert.Equal(3, before.EnumerateObject().Count());
        Assert.Equal(3, after.EnumerateObject().Count());
        Assert.Equal("Original station", before.GetProperty("name").GetString());
        Assert.Equal("Edited station", after.GetProperty("name").GetString());
        Assert.Equal("Original address", before.GetProperty("address").GetString());
        Assert.Equal("Original address", after.GetProperty("address").GetString());
        Assert.Equal("Original instructions", before.GetProperty("accessInstructions").GetString());
        Assert.Equal("Original instructions", after.GetProperty("accessInstructions").GetString());
        Assert.Equal(0, await context.SaveChangesAsync());
        Assert.Equal(1, await context.AuditEntries.CountAsync(audit => audit.ResourceId == stationId));
    }

    [Fact]
    public async Task SaveStation_ConcurrencyFailure_RetryAuditsReloadedValuesOnce()
    {
        Guid stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.User("https://identity.example", "operator")), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(connection, writer);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        station.Name = "Retried station";
        await using var owner = database.CreateConnection("platform_migrator");
        await owner.OpenAsync();
        await using var concurrentUpdate = new NpgsqlCommand(
            "UPDATE charging.stations SET name = 'Concurrent station' WHERE id = $1", owner);
        concurrentUpdate.Parameters.AddWithValue(stationId);
        await concurrentUpdate.ExecuteNonQueryAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        Assert.Equal(0, await context.AuditEntries.CountAsync(audit => audit.ResourceId == stationId));
        await context.Entry(station).ReloadAsync();
        station.Name = "Retried station";
        await context.SaveChangesAsync();
        var audit = await context.AuditEntries.SingleAsync(audit => audit.ResourceId == stationId);
        Assert.Equal("Concurrent station", audit.Details.GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Retried station", audit.Details.GetProperty("after").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SaveStation_OutsideContentAllowlist_DoesNotAudit()
    {
        Guid stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(new TestActorAccessor(null), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(connection, writer);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        station.Latitude = 42;
        await context.SaveChangesAsync();
        Assert.Equal(0, await context.AuditEntries.CountAsync(audit => audit.ResourceId == stationId));
    }

    [Fact]
    public async Task SaveStation_MissingActor_RejectsContentUpdate()
    {
        Guid stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(new TestActorAccessor(null), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = CreateAuditedContext(connection, writer);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        station.Name = "Missing actor";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        await using var readerConnection = database.CreateConnection("platform_reader");
        await using var reader = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(readerConnection).Options);
        Assert.Equal(
            "Original station",
            await reader.Stations.Where(station => station.Id == stationId)
                .Select(station => station.Name)
                .SingleAsync());
        Assert.Equal(0, await reader.AuditEntries.CountAsync(audit => audit.ResourceId == stationId));
    }

    private static async Task WithAuditInsertDeniedAsync(NpgsqlConnection owner, Func<Task> action)
    {
        await using var revoke = new NpgsqlCommand(
            "REVOKE INSERT ON charging.audit_entries FROM platform_writer", owner);
        await revoke.ExecuteNonQueryAsync();
        try
        {
            await action();
        }
        finally
        {
            await using var grant = new NpgsqlCommand(
                "GRANT INSERT ON charging.audit_entries TO platform_writer", owner);
            await grant.ExecuteNonQueryAsync();
        }
    }

    private static PlatformDbContext CreateAuditedContext(NpgsqlConnection connection, AuditEntryWriter writer) => new(
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(writer))
            .Options);

    private async Task<Guid> SeedStationAsync()
    {
        await using var connection = database.CreateConnection("platform_migrator");
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection).Options);
        var station = new Station
        {
            Name = "Original station",
            Address = "Original address",
            AccessInstructions = "Original instructions",
            TimeZoneId = "UTC"
        };
        context.Stations.Add(station);
        await context.SaveChangesAsync();
        return station.Id;
    }

    private sealed class TestActorAccessor(AuditActor? actor) : IAuditActorAccessor
    {
        public AuditActor GetActor() => actor ?? throw new InvalidOperationException("An explicit actor is required.");
    }
}
