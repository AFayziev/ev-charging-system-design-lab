using EvCharging.Api.Authentication;
using EvCharging.Api.Configuration;
using EvCharging.Api.Errors;
using EvCharging.Contracts.Charging;
using EvCharging.Domain.Charging;
using EvCharging.Persistence;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;

namespace EvCharging.Api.Features.Charging;

public sealed record CheckChargingStartCommand(Guid ReservationId, Guid OperationId)
    : IAuthenticatedRequest<CheckChargingStartResponse>;

public sealed record CheckChargingStartResponse(StartOperationResponse Operation, ChargingStartCheckOutcome Outcome);

public sealed class CheckChargingStartCommandHandler(
    PlatformDbContext database,
    IMessageBus bus,
    IOptions<ChargingStatusCheckOptions> options,
    ILogger<CheckChargingStartCommandHandler> logger)
{
    [NonTransactional]
    public async Task<Result<CheckChargingStartResponse>> HandleAsync(
        CheckChargingStartCommand command, UserIdentity identity, CancellationToken cancellationToken)
    {
        var operation = await database.Operations.AsNoTracking()
            .Include(value => value.Session!)
            .ThenInclude(value => value.Reservation!)
            .ThenInclude(value => value.Driver)
            .SingleOrDefaultAsync(
                value => value.PublicId == command.OperationId
                    && value.Session!.Reservation!.PublicId == command.ReservationId
                    && value.Kind == ChargingCommandKind.Start,
                cancellationToken);
        if (operation is null)
        {
            return Result.Fail<CheckChargingStartResponse>(
                new NotFoundError<Guid>("start operation", command.OperationId));
        }

        var driver = operation.Session!.Reservation!.Driver;
        if (driver is null || driver.IdentityIssuer != identity.Issuer || driver.IdentitySubject != identity.Subject)
        {
            return Result.Fail<CheckChargingStartResponse>(new ForbiddenError());
        }

        var outcome = ChargingStartCheckOutcome.Resolved;
        if (operation.Outcome is CommandOutcome.Pending or CommandOutcome.Uncertain)
        {
            try
            {
                var result = await bus.InvokeAsync<ChargingStartCheckResult>(
                    new CheckChargingStart(operation.Id), cancellationToken, options.Value.Timeout);
                outcome = result.Outcome;
            }
            catch (TimeoutException exception)
            {
                logger.LogWarning(exception, "Status check for start {OperationId} timed out", operation.PublicId);
                outcome = ChargingStartCheckOutcome.Unavailable;
            }

            // The worker may have committed an outcome while this request was waiting.
            operation = await database.Operations.AsNoTracking().Include(value => value.Session)
                .SingleAsync(value => value.Id == operation.Id, cancellationToken);
        }

        var response = await operation.ToResponseAsync(database, cancellationToken);
        if (operation.Outcome is CommandOutcome.Confirmed or CommandOutcome.Rejected)
        {
            outcome = ChargingStartCheckOutcome.Resolved;
        }

        return Result.Ok(new CheckChargingStartResponse(response, outcome));
    }
}
