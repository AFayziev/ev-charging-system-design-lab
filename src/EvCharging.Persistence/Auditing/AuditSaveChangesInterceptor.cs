using System.Text.Json;
using EvCharging.Domain.Auditing;
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

        // Concurrency failures may bypass the failure callback; rebuild audits for each attempt.
        DiscardAutomaticEntries(database);
        var entries = database.ChangeTracker.Entries<IAuditableEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        foreach (var entry in entries)
        {
            var properties = entry.Properties.Where(property => property.Metadata.IsAudited()).ToArray();
            if (properties.Length == 0
                || (entry.State == EntityState.Modified && !properties.Any(property => property.IsModified)))
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Added,
                EntityState.Modified => AuditAction.Updated,
                EntityState.Deleted => AuditAction.Deleted,
                _ => throw new InvalidOperationException($"Unsupported audit state {entry.State}.")
            };
            object details = entry.State switch
            {
                EntityState.Added => new AuditCreationDetails(Snapshot(properties, useOriginalValues: false)),
                EntityState.Modified => new AuditUpdateDetails(
                    Snapshot(properties, useOriginalValues: true), Snapshot(properties, useOriginalValues: false)),
                EntityState.Deleted => new AuditDeletionDetails(Snapshot(properties, useOriginalValues: true)),
                _ => throw new InvalidOperationException($"Unsupported audit state {entry.State}.")
            };
            _automaticEntries.Add(writer.Append(
                database, action, entry.Metadata.GetAuditResourceType(), entry.Entity.Id, details, 1));
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
            foreach (var audit in _automaticEntries)
            {
                context.Entry(audit).State = EntityState.Detached;
            }
        }

        _automaticEntries.Clear();
    }

    private sealed record AuditCreationDetails(IReadOnlyDictionary<string, JsonElement> After);

    private sealed record AuditUpdateDetails(
        IReadOnlyDictionary<string, JsonElement> Before,
        IReadOnlyDictionary<string, JsonElement> After);

    private sealed record AuditDeletionDetails(IReadOnlyDictionary<string, JsonElement> Before);
}
