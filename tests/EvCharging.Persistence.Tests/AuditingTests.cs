using System.Diagnostics;
using EvCharging.Persistence.Auditing;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace EvCharging.Persistence.Tests;

public sealed class AuditingTests(PlatformDatabaseFixture database) : IClassFixture<PlatformDatabaseFixture>
{
    [Fact]
    public async Task SaveStation_AuditInsertFailure_RollsBackAndRetryAuditsOnce()
    {
        long stationId = await SeedStationAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.User("https://identity.example", "operator")),
            clock);
        await using var writerConnection = database.CreateConnection("platform_writer");
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(writerConnection).AddInterceptors(new AuditSaveChangesInterceptor(writer)).Options);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        station.Name = "Edited station";

        await using var ownerConnection = database.CreateConnection("platform_migrator");
        await ownerConnection.OpenAsync();
        await using var revoke = new NpgsqlCommand(
            "REVOKE INSERT ON charging.audit_entries FROM platform_writer",
            ownerConnection);
        await revoke.ExecuteNonQueryAsync();
        try
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
        }
        finally
        {
            await using var grant = new NpgsqlCommand(
                "GRANT INSERT ON charging.audit_entries TO platform_writer",
                ownerConnection);
            await grant.ExecuteNonQueryAsync();
        }

        using var trace = new Activity("station-edit").SetIdFormat(ActivityIdFormat.W3C).Start();
        await context.SaveChangesAsync();
        var audit = await context.AuditEntries.SingleAsync(audit => audit.ResourceId == stationId);
        Assert.True(audit.Id > 0);
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
        long stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(
            new TestActorAccessor(AuditActor.User("https://identity.example", "operator")), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection).AddInterceptors(new AuditSaveChangesInterceptor(writer)).Options);
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
        long stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(new TestActorAccessor(null), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection).AddInterceptors(new AuditSaveChangesInterceptor(writer)).Options);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        station.Latitude = 42;
        await context.SaveChangesAsync();
        Assert.Equal(0, await context.AuditEntries.CountAsync(audit => audit.ResourceId == stationId));
    }

    [Fact]
    public async Task SaveStation_MissingActor_RejectsContentUpdate()
    {
        long stationId = await SeedStationAsync();
        var writer = new AuditEntryWriter(new TestActorAccessor(null), new FakeTimeProvider());
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection).AddInterceptors(new AuditSaveChangesInterceptor(writer)).Options);
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

    [Fact]
    public async Task AppendStationAudit_ServiceActor_PreservesExplicitMetadataAndAtomicUpdate()
    {
        long stationId = await SeedStationAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 13, 0, 0, TimeSpan.Zero));
        var writer = new AuditEntryWriter(new TestActorAccessor(AuditActor.Service("station-content-importer")), clock);
        await using var connection = database.CreateConnection("platform_writer");
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connection).Options);
        var station = await context.Stations.SingleAsync(station => station.Id == stationId);
        var before = new ContentSnapshot(station.Name, station.Address, station.AccessInstructions);
        station.Name = "Imported station";
        var after = new ContentSnapshot(station.Name, station.Address, station.AccessInstructions);
        writer.Append(
            context,
            AuditAction.Updated,
            AuditResourceType.Station,
            stationId,
            new ContentChange(before, after),
            1);

        await using var owner = database.CreateConnection("platform_migrator");
        await owner.OpenAsync();
        await using var revoke = new NpgsqlCommand(
            "REVOKE INSERT ON charging.audit_entries FROM platform_writer",
            owner);
        await revoke.ExecuteNonQueryAsync();
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
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
        finally
        {
            await using var grant = new NpgsqlCommand(
                "GRANT INSERT ON charging.audit_entries TO platform_writer",
                owner);
            await grant.ExecuteNonQueryAsync();
        }

        await context.SaveChangesAsync();
        var audit = await context.AuditEntries.SingleAsync(audit => audit.ResourceId == stationId);
        Assert.Equal(AuditActorType.Service, audit.ActorType);
        Assert.Null(audit.ActorIssuer);
        Assert.Equal("station-content-importer", audit.ActorSubject);
        Assert.Equal(clock.GetUtcNow(), audit.OccurredAt);
        Assert.Equal("Original station", audit.Details.GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Imported station", audit.Details.GetProperty("after").GetProperty("name").GetString());
    }

    private async Task<long> SeedStationAsync()
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

    private sealed record ContentChange(ContentSnapshot Before, ContentSnapshot After);

    private sealed record ContentSnapshot(
        string Name,
        string Address,
        string AccessInstructions);
}
