using EvCharging.WattHarborProviderApi.Persistence;

namespace EvCharging.WattHarborProviderApi.Features.Commands;

internal sealed record CommandResponse(
    Guid Id,
    Guid ExternalOperationId,
    CommandKind Kind,
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
            command.ExternalOperationId,
            command.Kind,
            applied ? CommandOutcome.Applied : CommandOutcome.Rejected,
            command.Session?.PublicId,
            applied ? command.CompletedAt : null,
            command.RejectionCode);
    }
}
