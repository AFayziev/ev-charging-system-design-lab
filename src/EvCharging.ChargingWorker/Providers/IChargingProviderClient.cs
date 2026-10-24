using FluentResults;

namespace EvCharging.ChargingWorker.Providers;

public interface IChargingProviderClient : IDisposable
{
    Task<Result<ProviderCommandResult?>> GetCommandByExternalOperationIdAsync(
        Guid externalOperationId, CancellationToken cancellationToken);
    Task<Result<ProviderSessionResult?>> GetSessionByExternalSessionIdAsync(
        Guid externalSessionId, CancellationToken cancellationToken);

    /// <summary>Reads the latest observation the provider recorded for the session, or null when it has none.</summary>
    Task<Result<ProviderSnapshotResult?>> GetSessionSnapshotAsync(
        Guid externalSessionId, CancellationToken cancellationToken);

    Task<Result<ProviderCommandResult>> StartAsync(
        Guid operationPublicId,
        Guid sessionPublicId,
        ProviderEquipmentAddress equipment,
        CancellationToken cancellationToken);

    Task<Result<ProviderCommandResult>> StopAsync(
        Guid operationPublicId,
        Guid sessionPublicId,
        CancellationToken cancellationToken);
}
