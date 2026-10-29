using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using EvCharging.Testing;
using Xunit;

namespace EvCharging.S3ObjectStore.Tests;

[Collection(ObjectStoreCollection.Name)]
public sealed class ObjectStoreTests(ObjectStoreFixture objectStore)
{
    private const string ImageType = "image/jpeg";
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("synthetic image bytes");
    [Fact]
    public async Task CreateUploadUrl_MatchingPut_StoresWhatVerificationReports()
    {
        var key = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length, UploadDeadline);

        var response = await objectStore.PutAsync(upload.Url, Bytes, ImageType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await objectStore.MediaWorkerStore.GetPrivateObjectAsync(key, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(Bytes.Length, stored.Length);
        Assert.Equal(ImageType, stored.ContentType);
        Assert.Equal(response.Headers.ETag?.Tag.Trim('"'), stored.ETag);
    }

    [Fact]
    public async Task CreateUploadUrl_DifferentContentType_IsRejected()
    {
        var key = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length, UploadDeadline);

        var response = await objectStore.PutAsync(upload.Url, Bytes, "image/png");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await objectStore.MediaWorkerStore.GetPrivateObjectAsync(key, CancellationToken.None));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task CreateUploadUrl_DifferentLength_IsRejectedWithoutStoring(int lengthDifference)
    {
        var key = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length, UploadDeadline);

        var response = await objectStore.PutAsync(upload.Url, new byte[Bytes.Length + lengthDifference], ImageType);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await objectStore.MediaWorkerStore.GetPrivateObjectAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task CreateDownloadUrl_StoredObject_DownloadsAsNamedAttachment()
    {
        var key = await UploadAsync();
        var download = await objectStore.ApiStore.CreateDownloadUrlAsync(key, "Session summary.pdf");

        var response = await objectStore.Browser.GetAsync(download.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Bytes, await response.Content.ReadAsByteArrayAsync());
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Equal("Session summary.pdf", disposition?.FileNameStar);
    }

    [Fact]
    public async Task PutPublic_ExistingKey_KeepsPublishedBytes()
    {
        var key = PublicKey();
        using var first = new MemoryStream(Bytes);
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("replacement"));

        Assert.True(await objectStore.MediaWorkerStore.PutPublicAsync(key, first, ImageType, CancellationToken.None));
        Assert.False(await objectStore.MediaWorkerStore.PutPublicAsync(key, second, ImageType, CancellationToken.None));

        Assert.Equal(Bytes, await objectStore.Browser.GetByteArrayAsync(objectStore.MediaWorkerStore.PublicUrl(key)));
    }

    [Fact]
    public async Task PutPublic_Content_IsReadableAnonymouslyAndCacheable()
    {
        var key = PublicKey();
        using var content = new MemoryStream(Bytes);
        await objectStore.MediaWorkerStore.PutPublicAsync(key, content, ImageType, CancellationToken.None);

        var response = await objectStore.Browser.GetAsync(objectStore.MediaWorkerStore.PublicUrl(key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ImageType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl?.Extensions.Select(header => header.Name) ?? []);
    }

    [Fact]
    public async Task ReadPrivate_StoredObject_ReturnsItsBytes()
    {
        var key = await UploadAsync();

        var content = await objectStore.MediaWorkerStore.ReadPrivateAsync(key, Bytes.Length, CancellationToken.None);

        Assert.Equal(Bytes, content);
    }

    [Fact]
    public async Task ReadPrivate_MissingKey_ReturnsNull() =>
        Assert.Null(await objectStore.MediaWorkerStore.ReadPrivateAsync(
            UploadKey(), Bytes.Length, CancellationToken.None));

    [Fact]
    public async Task ReadPrivate_ObjectLargerThanExpected_IsNotRead()
    {
        var key = await UploadAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => objectStore.MediaWorkerStore.ReadPrivateAsync(
            key, Bytes.Length - 1, CancellationToken.None));
    }

    [Fact]
    public async Task DeletePublicPrefix_Prefix_RemovesOnlyThatPhotosObjects()
    {
        var removed = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var station = Guid.NewGuid();
        var removedKeys = StationMediaObjectKeys.Variants
            .Select(variant => StationMediaObjectKeys.Variant(station, removed, variant))
            .ToArray();
        var keptKey = StationMediaObjectKeys.Variant(station, kept, StationMediaObjectKeys.ThumbnailVariant);
        foreach (var key in removedKeys.Append(keptKey))
        {
            using var content = new MemoryStream(Bytes);
            await objectStore.MediaWorkerStore.PutPublicAsync(key, content, ImageType, CancellationToken.None);
        }

        await objectStore.MediaWorkerStore.DeletePublicPrefixAsync(
            StationMediaObjectKeys.VariantPrefix(station, removed), CancellationToken.None);

        foreach (var key in removedKeys)
        {
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await objectStore.Browser.GetAsync(objectStore.MediaWorkerStore.PublicUrl(key))).StatusCode);
        }

        Assert.Equal(
            Bytes,
            await objectStore.Browser.GetByteArrayAsync(
                objectStore.MediaWorkerStore.PublicUrl(keptKey)));
    }

    [Fact]
    public async Task DeletePrivate_StoredAndMissingObjects_LeavesNothing()
    {
        var key = await UploadAsync();

        await objectStore.MediaWorkerStore.DeletePrivateAsync(key, CancellationToken.None);
        await objectStore.MediaWorkerStore.DeletePrivateAsync(key, CancellationToken.None);

        Assert.Null(await objectStore.MediaWorkerStore.GetPrivateObjectAsync(key, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "InternalError", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable", true)]
    [InlineData(HttpStatusCode.OK, "SlowDown", true)]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied", false)]
    [InlineData(HttpStatusCode.NotFound, "NoSuchKey", false)]
    public void IsTransientStorageFailure_StorageResponse_IsTransientOnlyWhenRepeatingCanHelp(
        HttpStatusCode status, string code, bool transient) =>
        Assert.Equal(
            transient,
            new AmazonS3Exception("failed", ErrorType.Unknown, code, "request", status).IsTransientStorageFailure());

    [Fact]
    public void IsTransientStorageFailure_BatchDeleteThatSkippedKeys_IsTransient() =>
        Assert.True(new DeleteObjectsException(new DeleteObjectsResponse()).IsTransientStorageFailure());

    [Fact]
    public void IsTransientStorageFailure_ConnectionEndedMidRead_IsTransient() =>
        Assert.True(new EndOfStreamException().IsTransientStorageFailure());

    [Fact]
    public void IsTransientStorageFailure_NetworkFailure_IsTransient() =>
        Assert.True(new AmazonServiceException(new HttpRequestException("refused")).IsTransientStorageFailure());

    [Fact]
    public async Task AnonymousAccess_PrivateObjectAndPublicListing_AreDenied()
    {
        var key = await UploadAsync();

        var privateRead = await objectStore.Browser.GetAsync(
            new Uri(objectStore.ServiceUrl, $"{ObjectStoreFixture.PrivateBucket}/{key}"));
        var publicListing = await objectStore.Browser.GetAsync(
            new Uri(objectStore.ServiceUrl, $"{ObjectStoreFixture.PublicBucket}?list-type=2"));

        Assert.Equal(HttpStatusCode.Forbidden, privateRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publicListing.StatusCode);
    }

    [Fact]
    public async Task ApiFilesUser_PublicWriteAndListingOutsideUploads_AreDenied()
    {
        var publicWrite = await Assert.ThrowsAsync<AmazonS3Exception>(() => objectStore.ApiClient.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = ObjectStoreFixture.PublicBucket,
                Key = PublicKey(),
                ContentBody = "denied"
            }));
        var listing = await Assert.ThrowsAsync<AmazonS3Exception>(() => objectStore.ApiClient.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = ObjectStoreFixture.PrivateBucket }));

        Assert.Equal(HttpStatusCode.Forbidden, publicWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listing.StatusCode);
    }

    [Theory]
    [InlineData(ObjectStoreFixture.WebOrigin, true)]
    [InlineData("http://elsewhere.example", false)]
    public async Task UploadPreflight_Origin_IsAllowedOnlyForTheApplication(string origin, bool allowed)
    {
        using var preflight = new HttpRequestMessage(
            HttpMethod.Options,
            new Uri(objectStore.ServiceUrl, $"{ObjectStoreFixture.PrivateBucket}/{UploadKey()}"));
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await objectStore.Browser.SendAsync(preflight);

        var allowedOrigin = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.Single()
            : null;
        Assert.Equal(allowed ? origin : null, allowedOrigin);
    }

    private static DateTimeOffset UploadDeadline => DateTimeOffset.UtcNow.AddMinutes(30);

    private static string UploadKey() => $"{ObjectStore.UploadPrefix}{Guid.NewGuid():N}";

    private static string PublicKey() => $"stations/{Guid.NewGuid():N}/display.webp";

    private async Task<string> UploadAsync()
    {
        var key = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length, UploadDeadline);
        (await objectStore.PutAsync(upload.Url, Bytes, ImageType)).EnsureSuccessStatusCode();
        return key;
    }
}
