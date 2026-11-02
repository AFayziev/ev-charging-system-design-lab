using FluentResults;

namespace EvCharging.Api.Errors;

internal sealed class ConcurrencyConflictError : Error
{
    public const string Code = "concurrency_conflict";

    public ConcurrencyConflictError()
        : base("The resource changed after it was loaded. Reload it before applying your changes.")
    {
    }
}
