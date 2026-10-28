using System.Net;
using System.Text;
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
        var stored = await objectStore.ApiStore.GetPrivateObjectAsync(key, CancellationToken.None);
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
        Assert.Null(await objectStore.ApiStore.GetPrivateObjectAsync(key, CancellationToken.None));
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
        Assert.Null(await objectStore.ApiStore.GetPrivateObjectAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task GetPrivateObject_MissingKey_ReturnsNull() =>
        Assert.Null(await objectStore.ApiStore.GetPrivateObjectAsync(UploadKey(), CancellationToken.None));

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
    public async Task CopyPrivateToPublic_StoredObject_IsReadableAnonymouslyAndCacheable()
    {
        var publicKey = PublicKey();
        await objectStore.MediaWorkerStore.CopyPrivateToPublicAsync(
            await UploadAsync(), publicKey, ImageType, CancellationToken.None);

        var response = await objectStore.Browser.GetAsync(objectStore.MediaWorkerStore.PublicUrl(publicKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ImageType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl?.Extensions.Select(header => header.Name) ?? []);
    }

    [Fact]
    public async Task CopyPrivateToPublic_ExistingKey_KeepsPublishedBytes()
    {
        var key = PublicKey();
        using var published = new MemoryStream(Bytes);
        await objectStore.MediaWorkerStore.PutPublicAsync(key, published, ImageType, CancellationToken.None);
        var replacement = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(replacement, ImageType, 11, UploadDeadline);
        (await objectStore.PutAsync(upload.Url, Encoding.UTF8.GetBytes("replacement"), ImageType))
            .EnsureSuccessStatusCode();

        var copied = await objectStore.MediaWorkerStore.CopyPrivateToPublicAsync(
            replacement, key, ImageType, CancellationToken.None);

        Assert.False(copied);
        Assert.Equal(Bytes, await objectStore.Browser.GetByteArrayAsync(objectStore.MediaWorkerStore.PublicUrl(key)));
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
    public async Task CopyPrivate_VerifiedSource_StoresItUnderTheNewKey()
    {
        var source = await UploadAsync();
        var verified = await objectStore.ApiStore.GetPrivateObjectAsync(source, CancellationToken.None);
        var destination = OriginalKey();

        var copied = await objectStore.ApiStore.CopyPrivateAsync(
            source, destination, verified!.ETag, CancellationToken.None);

        Assert.True(copied);
        var stored = await objectStore.ApiStore.GetPrivateObjectAsync(destination, CancellationToken.None);
        Assert.Equal((Bytes.Length, ImageType), (stored?.Length, stored?.ContentType));
    }

    [Fact]
    public async Task CopyPrivate_ExistingDestination_KeepsItsBytes()
    {
        var first = await UploadAsync();
        var firstETag = (await objectStore.ApiStore.GetPrivateObjectAsync(first, CancellationToken.None))!.ETag;
        var destination = OriginalKey();
        await objectStore.ApiStore.CopyPrivateAsync(first, destination, firstETag, CancellationToken.None);
        var second = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(second, ImageType, 11, UploadDeadline);
        (await objectStore.PutAsync(upload.Url, Encoding.UTF8.GetBytes("replacement"), ImageType))
            .EnsureSuccessStatusCode();
        var secondETag = (await objectStore.ApiStore.GetPrivateObjectAsync(second, CancellationToken.None))!.ETag;

        var copied = await objectStore.ApiStore.CopyPrivateAsync(
            second, destination, secondETag, CancellationToken.None);

        Assert.False(copied);
        var kept = await objectStore.ApiStore.GetPrivateObjectAsync(destination, CancellationToken.None);
        Assert.Equal(firstETag, kept?.ETag);
    }

    [Fact]
    public async Task CopyPrivate_SourceReplacedThroughTheSameUploadUrl_CopiesNothing()
    {
        var source = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(source, ImageType, Bytes.Length, UploadDeadline);
        (await objectStore.PutAsync(upload.Url, Bytes, ImageType)).EnsureSuccessStatusCode();
        var verified = await objectStore.ApiStore.GetPrivateObjectAsync(source, CancellationToken.None);
        var replacement = Encoding.UTF8.GetBytes("REPLACED image bytes!");
        (await objectStore.PutAsync(upload.Url, replacement, ImageType)).EnsureSuccessStatusCode();
        var destination = OriginalKey();

        var copied = await objectStore.ApiStore.CopyPrivateAsync(
            source, destination, verified!.ETag, CancellationToken.None);

        Assert.False(copied);
        Assert.Null(await objectStore.ApiStore.GetPrivateObjectAsync(destination, CancellationToken.None));
    }

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

    // Listing under uploads/ lets stores that follow AWS report a missing upload as 404 rather than 403.
    [Fact]
    public async Task ApiFilesUser_ListingUploads_IsAllowed()
    {
        var key = await UploadAsync();

        var listing = await objectStore.ApiClient.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = ObjectStoreFixture.PrivateBucket,
            Prefix = key
        });

        Assert.Equal(key, Assert.Single(listing.S3Objects ?? []).Key);
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

    [Fact]
    public async Task PrivateBucketLifecycle_ExpiresOnlyUnverifiedUploads()
    {
        var lifecycle = await objectStore.RootClient.GetLifecycleConfigurationAsync(
            ObjectStoreFixture.PrivateBucket);

        var rule = Assert.Single(lifecycle.Configuration.Rules);
        Assert.Equal(LifecycleRuleStatus.Enabled, rule.Status);
        Assert.Equal(
            ObjectStore.UploadPrefix,
            rule.Filter.LifecycleFilterPredicate switch
            {
                LifecyclePrefixPredicate prefix => prefix.Prefix,
                _ => rule.Filter.Prefix
            });
        Assert.Equal(ObjectStoreFixture.UploadExpiryDays, rule.Expiration.Days);
    }

    private static DateTimeOffset UploadDeadline => DateTimeOffset.UtcNow.AddMinutes(30);

    private static string UploadKey() => $"{ObjectStore.UploadPrefix}{Guid.NewGuid():N}";

    private static string OriginalKey() =>
        StationMediaObjectKeys.Original(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static string PublicKey() => $"stations/{Guid.NewGuid():N}/display.webp";

    private async Task<string> UploadAsync()
    {
        var key = UploadKey();
        var upload = await objectStore.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length, UploadDeadline);
        (await objectStore.PutAsync(upload.Url, Bytes, ImageType)).EnsureSuccessStatusCode();
        return key;
    }
}
