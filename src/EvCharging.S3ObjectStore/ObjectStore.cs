using System.Net;
using System.Net.Http.Headers;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EvCharging.S3ObjectStore;

public sealed partial class ObjectStore(
    ObjectStoreClients clients,
    IOptions<ObjectStoreOptions> options,
    TimeProvider time,
    ILogger<ObjectStore> logger)
{
    /// <summary>Pending browser uploads. Storage lifecycle rules expire this prefix.</summary>
    public const string UploadPrefix = "uploads/";

    // Published keys are fixed per photo and variant and never rewritten, so caches may keep them indefinitely.
    private const string PublishedCacheControl = "public, max-age=31536000, immutable";

    private readonly AmazonS3Client _client = clients.Requests;
    private readonly ObjectStoreOptions _options = options.Value;

    /// <summary>
    /// Signs a single PUT of exactly <paramref name="contentLength"/> bytes of <paramref name="contentType"/>
    /// to <paramref name="key"/> in the private bucket until <paramref name="expiresAt"/>, the deadline the caller
    /// recorded for the upload. Storage rejects any other type or size.
    /// </summary>
    public async Task<SignedTransfer> CreateUploadUrlAsync(
        string key, string contentType, long contentLength, DateTimeOffset expiresAt)
    {
        if (!key.StartsWith(UploadPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Upload keys start with '{UploadPrefix}'.", nameof(key));
        }

        // An empty type would leave Content-Type out of the signature, letting the upload carry any type.
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentLength);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAt, time.GetUtcNow());
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.PrivateBucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Protocol = clients.SigningProtocol,
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime
        };
        request.Headers.ContentLength = contentLength;
        var url = new Uri(await clients.Signing.GetPreSignedURLAsync(request));
        LogUploadSigned(key, contentType, contentLength, expiresAt);
        return new SignedTransfer(url, expiresAt);
    }

    /// <summary>Signs a GET of a private object that browsers save as <paramref name="fileName"/>.</summary>
    public async Task<SignedTransfer> CreateDownloadUrlAsync(string key, string fileName)
    {
        var expiresAt = time.GetUtcNow() + _options.DownloadUrlLifetime;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.PrivateBucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = clients.SigningProtocol,
            Expires = expiresAt.UtcDateTime
        };
        request.ResponseHeaderOverrides.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName }.ToString();
        var url = new Uri(await clients.Signing.GetPreSignedURLAsync(request));
        LogDownloadSigned(key, expiresAt);
        return new SignedTransfer(url, expiresAt);
    }

    /// <summary>
    /// Reads what storage holds at a private key, or null when nothing is there. Stores that follow AWS report a
    /// missing key as not found only to callers allowed to list it; otherwise the call fails with access denied.
    /// </summary>
    public async Task<StoredObject?> GetPrivateObjectAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await _client.GetObjectMetadataAsync(_options.PrivateBucket, key, cancellationToken);
            return new StoredObject(
                metadata.ContentLength,
                string.IsNullOrEmpty(metadata.Headers.ContentType) ? null : metadata.Headers.ContentType,
                metadata.ETag.Trim('"'));
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            LogObjectMissing(exception, key, exception.ErrorCode, exception.RequestId);
            return null;
        }
    }

    /// <summary>
    /// Writes a published object unless the key already exists. Returns false when it does, which a retry of the
    /// same deterministic publication treats as already done.
    /// </summary>
    public async Task<bool> PutPublicAsync(
        string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            await _client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _options.PublicBucket,
                    Key = key,
                    InputStream = content,
                    AutoCloseStream = false,
                    ContentType = contentType,
                    Headers = { CacheControl = PublishedCacheControl },
                    IfNoneMatch = "*"
                },
                cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            LogPublicationExists(exception, key, exception.ErrorCode, exception.RequestId);
            return false;
        }
    }

    /// <summary>
    /// Reads a private object that the caller expects to be at most <paramref name="maximumLength"/> bytes, or
    /// returns null when nothing is stored there. Storage already enforces the signed upload's size, so a longer
    /// object means the key was written some other way and is not read.
    /// </summary>
    public async Task<byte[]?> ReadPrivateAsync(string key, long maximumLength, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_options.PrivateBucket, key, cancellationToken);
            if (response.ContentLength < 0 || response.ContentLength > maximumLength)
            {
                throw new InvalidDataException(
                    $"The object {key} is {response.ContentLength} bytes, not the at most {maximumLength} expected.");
            }

            var content = new byte[response.ContentLength];
            await response.ResponseStream.ReadExactlyAsync(content, cancellationToken);
            return content;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            LogObjectMissing(exception, key, exception.ErrorCode, exception.RequestId);
            return null;
        }
    }

    /// <summary>Deletes every published object under <paramref name="prefix"/>; absent objects are not an error.</summary>
    public async Task DeletePublicPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var listing = new ListObjectsV2Request { BucketName = _options.PublicBucket, Prefix = prefix };
        ListObjectsV2Response page;
        do
        {
            page = await _client.ListObjectsV2Async(listing, cancellationToken);
            if (page.S3Objects is { Count: > 0 })
            {
                await _client.DeleteObjectsAsync(
                    new DeleteObjectsRequest
                    {
                        BucketName = _options.PublicBucket,
                        Objects = page.S3Objects.Select(s3Object => new KeyVersion { Key = s3Object.Key }).ToList(),
                        Quiet = true
                    },
                    cancellationToken);
            }

            listing.ContinuationToken = page.NextContinuationToken;
        }
        while (page.IsTruncated == true);
    }

    /// <summary>Deletes a private object; an absent object is not an error.</summary>
    public Task DeletePrivateAsync(string key, CancellationToken cancellationToken) =>
        _client.DeleteObjectAsync(_options.PrivateBucket, key, cancellationToken);

    public Uri PublicUrl(string key) => new($"{_options.PublicBaseUrl.TrimEnd('/')}/{key}");

    // Signed URLs are credentials, so only the key and limits are logged.
    [LoggerMessage(
        LogLevel.Debug,
        "Signed upload of {ContentLength} bytes of {ContentType} to {ObjectKey}, valid until {ExpiresAt}")]
    private partial void LogUploadSigned(
        string objectKey, string contentType, long contentLength, DateTimeOffset expiresAt);

    [LoggerMessage(LogLevel.Debug, "Signed download of {ObjectKey}, valid until {ExpiresAt}")]
    private partial void LogDownloadSigned(string objectKey, DateTimeOffset expiresAt);

    // These handled outcomes end the storage exception here, so the log keeps its details.
    [LoggerMessage(
        LogLevel.Debug,
        "Private object {ObjectKey} was not found (AWS error {ErrorCode}, request {RequestId})")]
    private partial void LogObjectMissing(Exception exception, string objectKey, string? errorCode, string? requestId);

    [LoggerMessage(
        LogLevel.Information,
        "Public object {ObjectKey} already exists; the published bytes were left unchanged "
        + "(AWS error {ErrorCode}, request {RequestId})")]
    private partial void LogPublicationExists(
        Exception exception, string objectKey, string? errorCode, string? requestId);
}
