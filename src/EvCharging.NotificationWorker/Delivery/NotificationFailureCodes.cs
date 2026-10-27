namespace EvCharging.NotificationWorker.Delivery;

/// <summary>Failure codes recorded on delivery attempts, grouped by the context that produces them.</summary>
internal static class NotificationFailureCodes
{
    /// <summary>Transport and resilience failures common to every provider adapter.</summary>
    public static class Transport
    {
        public const string ConnectionNotEstablished = "connection_not_established";
        public const string CircuitOpen = "circuit_open";
        public const string SendTimeout = "send_timeout";
        public const string TransportFailure = "transport_failure";

        public static string Http(int statusCode) => "http_" + statusCode;
    }

    public static class Mailpit
    {
        public const string InvalidCaptureResponse = "invalid_capture_response";
    }

    public static class AmazonSes
    {
        public const string InvalidAcceptanceResponse = "invalid_acceptance_response";
        public const string Throttled = "throttled";
        public const string MessageRejected = "message_rejected";
        public const string SenderDomainNotVerified = "sender_domain_not_verified";
        public const string AccountSuspended = "account_suspended";
        public const string SendingPaused = "sending_paused";
        public const string LimitExceeded = "limit_exceeded";
    }
}
