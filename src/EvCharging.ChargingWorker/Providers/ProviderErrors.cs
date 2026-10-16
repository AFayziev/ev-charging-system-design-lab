using System.Net;
using FluentResults;

namespace EvCharging.ChargingWorker.Providers;

internal sealed class ProviderRejectedError(Guid commandId, Guid reference, string code)
    : Error("The provider definitively rejected the charging start.")
{
    public Guid CommandId { get; } = commandId;
    public Guid Reference { get; } = reference;
    public string Code { get; } = code;
}

internal sealed class ProviderResponseError(HttpStatusCode statusCode, string responseBody, string? code = null)
    : Error("The provider response does not establish a matching charging outcome.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
    public string? Code { get; } = code;
}

internal sealed class ProviderTransportError(bool timedOut)
    : Error(timedOut ? "The provider request timed out." : "The provider request could not be completed.")
{
    public bool TimedOut { get; } = timedOut;
}
