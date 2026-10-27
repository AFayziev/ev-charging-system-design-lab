using Amazon.Runtime;
using Amazon.SimpleEmailV2;

namespace EvCharging.NotificationWorker.Delivery;

/// <summary>Builds the SES client with transport and retry behavior owned by the application.</summary>
internal static class AmazonSesClient
{
    internal const string HttpClientName = "amazon-ses";

    // Credentials and region come from the SDK default chain (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION).
    public static IAmazonSimpleEmailServiceV2 Create(IServiceProvider services) =>
        new AmazonSimpleEmailServiceV2Client(new AmazonSimpleEmailServiceV2Config
        {
            // SendEmail has no idempotency key. SDK retries would repeat a send SES may already have accepted,
            // without an attempt record. Retry safety is decided from recorded evidence by the delivery job policy.
            MaxErrorRetry = 0,
            HttpClientFactory = new FactoryHttpClientFactory(services.GetRequiredService<IHttpClientFactory>())
        });

    private sealed class FactoryHttpClientFactory(IHttpClientFactory httpClients) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) =>
            httpClients.CreateClient(HttpClientName);

        // Let IHttpClientFactory own handler lifetime and rotation.
        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }
}
