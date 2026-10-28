using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace EvCharging.ObjectStorage;

/// <summary>
/// S3 clients for one set of credentials. Requests go to the endpoint the host reaches; signed URLs carry the
/// browser-facing endpoint, and signing makes no network call.
/// </summary>
public sealed class ObjectStorageClients : IDisposable
{
    public ObjectStorageClients(IOptions<ObjectStorageOptions> options)
    {
        var settings = options.Value;
        Requests = Create(settings, settings.ServiceUrl);
        Signing = settings.PublicServiceUrl is { } publicServiceUrl ? Create(settings, publicServiceUrl) : Requests;

        // Presigning defaults to HTTPS; signed URLs follow the browser-facing endpoint's scheme.
        SigningProtocol = new Uri(Signing.Config.ServiceURL).Scheme == Uri.UriSchemeHttp
            ? Protocol.HTTP
            : Protocol.HTTPS;
    }

    internal AmazonS3Client Requests { get; }

    internal AmazonS3Client Signing { get; }

    internal Protocol SigningProtocol { get; }

    public void Dispose()
    {
        Requests.Dispose();
        if (!ReferenceEquals(Signing, Requests))
        {
            Signing.Dispose();
        }
    }

    // The clients keep the SDK's own HTTP handling rather than an IHttpClientFactory client, so the shared
    // standard resilience handler does not stack on the SDK retries, which re-sign each attempt and classify
    // S3 throttling and transient errors.
    private static AmazonS3Client Create(ObjectStorageOptions options, string serviceUrl) =>
        new(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = options.Region,
                ForcePathStyle = true,

                // The default WHEN_SUPPORTED adds checksum trailers and signs an empty-body checksum into
                // presigned uploads, which S3-compatible stores such as RustFS and R2 reject.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
}
