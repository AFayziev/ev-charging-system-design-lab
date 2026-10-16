using EvCharging.SynchronousChargingProviderApi.Persistence;

namespace EvCharging.SynchronousChargingProviderApi.Features.Commands;

internal sealed record CommandResponse(
    Guid Id,
    Guid Reference,
    CommandOutcome Outcome,
    Guid? SessionId,
    DateTimeOffset? AppliedAt,
    string? Rejection);

internal static class CommandMappingExtensions
{
    public static CommandResponse ToResponse(this ProviderCommand command)
    {
        if (command.Applied is not { } applied)
        {
            throw new InvalidOperationException("An incomplete command has no authoritative outcome.");
        }

        return new(
            command.PublicId,
            command.Reference,
            applied ? CommandOutcome.Applied : CommandOutcome.Rejected,
            command.Session?.PublicId,
            applied ? command.CompletedAt : null,
            command.RejectionCode);
    }
}
