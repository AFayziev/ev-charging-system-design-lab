using FluentResults;

namespace EvCharging.Api.Errors;

internal sealed class ValidationError(string propertyName, string message) : Error(message)
{
    public string PropertyName { get; } = propertyName;
}
