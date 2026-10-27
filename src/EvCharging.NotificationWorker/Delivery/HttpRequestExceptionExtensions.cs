using System.Net.Sockets;

namespace EvCharging.NotificationWorker.Delivery;

internal static class HttpRequestExceptionExtensions
{
    /// <summary>The request provably never reached the server.</summary>
    public static bool IsConnectionNotEstablished(this HttpRequestException exception) =>
        exception.HttpRequestError == HttpRequestError.NameResolutionError
        || exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
}
