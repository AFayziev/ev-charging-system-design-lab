using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Api.Pagination;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using EvCharging.Persistence.Entities;
using EvCharging.Persistence.Extensions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Features.Charging;

public sealed record ListUnsettledOperationsRequest(Guid? AfterId = null, int Limit = CursorPagination.DefaultPageSize)
    : IAuthenticatedRequest<UnsettledOperationListResponse>;

public sealed record UnsettledOperationListResponse(
    IReadOnlyList<UnsettledOperationResponse> Operations,
    Guid? NextAfterId);

/// <summary>
/// An operation still awaiting evidence. The release window and eligibility are decided by the server, so the
/// operator's device clock does not matter.
/// </summary>
public sealed record UnsettledOperationResponse(
    Guid Id,
    ChargingCommandKind Kind,
    CommandOutcome Outcome,
    DateTimeOffset AcceptedAt,
    Guid SessionId,
    ChargingEquipmentResponse Equipment,
    DateTimeOffset LastRequestedAt,
    bool HasObservations,
    DateTimeOffset? ReleasableAt,
    bool CanRelease,
    IReadOnlyList<UnsettledOperationAttemptResponse> Attempts);

public sealed record ChargingEquipmentResponse(
    string StationName,
    string ChargerName,
    int ConnectorNumber);

public sealed record UnsettledOperationAttemptResponse(
    int Number,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset? FinishedAt,
    CommandOutcome? Outcome,
    string? FailureCode,
    int? ResponseStatusCode,
    string IntegrationKey,
    string ProviderStationId,
    string ProviderChargerId,
    string ProviderConnectorId,
    Guid? ProviderCommandId,
    Guid? ProviderSessionId);

public sealed class ListUnsettledOperationsQueryHandler(
    PlatformDbContext database,
    TimeProvider clock,
    IOptions<ChargingStartReleaseOptions> releaseOptions)
{
    public async Task<Result<UnsettledOperationListResponse>> HandleAsync(
        ListUnsettledOperationsRequest request, UserIdentity identity, CancellationToken cancellationToken)
    {
        if (CursorPagination.ValidateLimit(request.Limit, nameof(request.Limit)) is { } error)
        {
            return Result.Fail<UnsettledOperationListResponse>(error);
        }

        var assignedStationIds = database.AssignedStationIds(identity.Issuer, identity.Subject);
        var assignedOperations = database.Operations
            .AsNoTracking()
            .Where(operation => database.Connectors.Any(connector => connector.Id == operation.Session!.ConnectorId
                && assignedStationIds.Contains(connector.Charger!.StationId)));

        // The cursor's operation may have settled or been released since the previous page was read, so it is
        // looked up among all the operator's operations.
        var afterId = Guid.Empty;
        if (request.AfterId is not null)
        {
            var anchorId = await assignedOperations
                .Where(operation => operation.PublicId == request.AfterId)
                .Select(operation => (Guid?)operation.Id)
                .SingleOrDefaultAsync(cancellationToken);
                
            if (anchorId is null)
            {
                return Result.Fail<UnsettledOperationListResponse>(new ValidationError(
                    nameof(request.AfterId), "The cursor does not identify an operation on an assigned station."));
            }

            afterId = anchorId.Value;
        }

        // Constant outcomes keep the predicate matching the partial index that serves this list.
        var unsettledOperations = assignedOperations.Where(operation =>
            (operation.Outcome == CommandOutcome.Pending || operation.Outcome == CommandOutcome.Uncertain)
            && operation.AuthorizedAttemptCount > 0
            && operation.ReleasedAt == null);
        var page = await unsettledOperations
            .Where(operation => operation.Id.CompareTo(afterId) > 0)
            .OrderBy(operation => operation.Id)
            .Take(request.Limit + 1)
            .Select(operation => new UnsettledOperationRow(
                operation,
                operation.Session!.PublicId,
                database.SessionEvidence.Any(evidence => evidence.SessionId == operation.SessionId),
                database.Connectors
                    .Where(connector => connector.Id == operation.Session.ConnectorId)
                    .Select(connector => new ChargingEquipmentResponse(
                        connector.Charger!.Station!.Name, connector.Charger.Name, connector.Number))
                    .First()))
            .ToListAsync(cancellationToken);
        var nextAfterId = CursorPagination.TrimPage(page, request.Limit, row => row.Operation.PublicId);
        var operations = await ToResponsesAsync(page, cancellationToken);

        return Result.Ok(new UnsettledOperationListResponse(operations, nextAfterId));
    }

    /// <summary>Builds the page's responses, reading the attempts of all its operations in one query.</summary>
    private async Task<List<UnsettledOperationResponse>> ToResponsesAsync(
        List<UnsettledOperationRow> rows, CancellationToken cancellationToken)
    {
        var operationIds = rows.Select(row => row.Operation.Id).ToList();
        var attempts = await database.Attempts.AsNoTracking()
            .Where(attempt => operationIds.Contains(attempt.OperationId))
            .OrderBy(attempt => attempt.Number)
            .ToListAsync(cancellationToken);
        var attemptsByOperation = attempts.ToLookup(attempt => attempt.OperationId);

        var now = clock.GetUtcNow();
        var releaseWindow = releaseOptions.Value.ReleaseWindow;
        var responses = new List<UnsettledOperationResponse>(rows.Count);

        foreach (var row in rows)
        {
            var operation = row.Operation;
            var operationAttempts = attemptsByOperation[operation.Id].ToList();
            var lastRequestedAt = operationAttempts[^1].AuthorizedAt;
            bool canRelease = operation
                .ValidateStartRelease(now, lastRequestedAt, releaseWindow, row.HasObservations)
                .IsSuccess;

            responses.Add(new UnsettledOperationResponse(
                operation.PublicId,
                operation.Kind,
                operation.Outcome,
                operation.AcceptedAt,
                row.SessionPublicId,
                row.Equipment,
                lastRequestedAt,
                row.HasObservations,
                operation.StartReleasableAt(lastRequestedAt, releaseWindow),
                canRelease,
                operationAttempts.Select(ToAttemptResponse).ToList()));
        }

        return responses;
    }

    private static UnsettledOperationAttemptResponse ToAttemptResponse(Attempt attempt) =>
        new(
            attempt.Number,
            attempt.AuthorizedAt,
            attempt.FinishedAt,
            attempt.Outcome,
            attempt.FailureCode,
            attempt.ResponseStatusCode,
            attempt.IntegrationKey,
            attempt.ProviderStationId,
            attempt.ProviderChargerId,
            attempt.ProviderConnectorId,
            attempt.ProviderCommandId,
            attempt.ProviderSessionId);

    private sealed record UnsettledOperationRow(
        Operation Operation,
        Guid SessionPublicId,
        bool HasObservations,
        ChargingEquipmentResponse Equipment);
}
