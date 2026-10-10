using System.Text.Json;
using EvCharging.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EvCharging.Persistence.Auditing;

public sealed class AuditSaveChangesInterceptor(AuditEntryWriter writer) : SaveChangesInterceptor
{
    private readonly List<AuditEntry> _automaticEntries = [];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        AppendAutomaticEntries(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AppendAutomaticEntries(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        _automaticEntries.Clear();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        _automaticEntries.Clear();
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        DiscardAutomaticEntries(eventData.Context);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardAutomaticEntries(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) =>
        DiscardAutomaticEntries(eventData.Context);

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardAutomaticEntries(eventData.Context);
        return Task.CompletedTask;
    }

    private void AppendAutomaticEntries(DbContext? context)
    {
        if (context is not PlatformDbContext database)
        {
            return;
        }

        // Concurrency failures do not invoke EF's interceptor failure callback.
        // A new save attempt must discard any automatic entries left by that attempt.
        DiscardAutomaticEntries(database);

        // Inspect a snapshot before appending entries to the same change tracker.
        var entries = database.ChangeTracker.Entries<IAuditableEntity>()
            .Where(entry => entry.State == EntityState.Modified)
            .ToArray();
        foreach (var entry in entries)
        {
            var properties = entry.Properties.Where(property => property.Metadata.IsAudited()).ToArray();
            if (!properties.Any(property => property.IsModified))
            {
                continue;
            }

            var resourceType = entry.Metadata.GetAuditResourceType();
            var details = new AuditUpdateDetails(
                Snapshot(properties, useOriginalValues: true), Snapshot(properties, useOriginalValues: false));
            _automaticEntries.Add(writer.Append(
                database, AuditAction.Updated, resourceType, entry.Entity.Id, details, 1));
        }
    }

    private static Dictionary<string, JsonElement> Snapshot(
        IEnumerable<PropertyEntry> properties, bool useOriginalValues) =>
        properties.ToDictionary(
            property => JsonNamingPolicy.CamelCase.ConvertName(property.Metadata.Name),
            property => JsonSerializer.SerializeToElement(
                useOriginalValues ? property.OriginalValue : property.CurrentValue,
                property.Metadata.ClrType,
                JsonSerializerOptions.Web),
            StringComparer.Ordinal);

    private void DiscardAutomaticEntries(DbContext? context)
    {
        if (context is not null)
        {
            // EF retains Added entries after rollback; regenerate only automatic audits on a new save attempt.
            foreach (var audit in _automaticEntries)
            {
                context.Entry(audit).State = EntityState.Detached;
            }
        }

        _automaticEntries.Clear();
    }

    private sealed record AuditUpdateDetails(
        IReadOnlyDictionary<string, JsonElement> Before,
        IReadOnlyDictionary<string, JsonElement> After);
}
