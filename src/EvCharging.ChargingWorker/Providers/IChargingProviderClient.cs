using FluentResults;

namespace EvCharging.ChargingWorker.Providers;

public interface IChargingProviderClient : IDisposable
{
    Task<Result<ProviderCommandResult?>> GetCommandByExternalOperationIdAsync(
        Guid externalOperationId, CancellationToken cancellationToken);
    Task<Result<ProviderSessionResult?>> GetSessionByExternalSessionIdAsync(
        Guid externalSessionId, CancellationToken cancellationToken);

    Task<Result<ProviderCommandResult>> StartAsync(
        Guid operationPublicId,
        Guid sessionPublicId,
        ProviderEquipmentAddress equipment,
        CancellationToken cancellationToken);
}
