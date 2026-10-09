using FluentResults;

namespace EvCharging.Api.Errors;

internal sealed class NotFoundError<TId>(string resourceName, TId resourceId)
    : Error($"The {resourceName} was not found.")
{
    public string ResourceName { get; } = resourceName;

    public TId ResourceId { get; } = resourceId;
}
