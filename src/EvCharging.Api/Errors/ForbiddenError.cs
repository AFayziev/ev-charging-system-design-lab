using FluentResults;

namespace EvCharging.Api.Errors;

internal sealed class ForbiddenError : Error
{
    public ForbiddenError() : base("The resource is not assigned to this operator.")
    {
    }
}
