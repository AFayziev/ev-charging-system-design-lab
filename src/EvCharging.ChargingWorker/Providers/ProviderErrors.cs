using System.Net;
using FluentResults;

namespace EvCharging.ChargingWorker.Providers;

internal sealed class ProviderRejectedError(
    Guid? commandId,
    Guid externalOperationId,
    string? code,
    HttpStatusCode? statusCode = null,
    string? responseBody = null)
    : Error("The provider definitively did not apply the charging start.")
{
    public Guid? CommandId { get; } = commandId;
    public Guid ExternalOperationId { get; } = externalOperationId;
    public string? Code { get; } = code;
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public string? ResponseBody { get; } = responseBody;
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
