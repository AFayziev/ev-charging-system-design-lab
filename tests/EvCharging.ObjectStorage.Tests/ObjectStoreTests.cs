using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace EvCharging.ObjectStorage.Tests;

[Collection(ObjectStorageCollection.Name)]
public sealed class ObjectStoreTests(ProvisionedStorageFixture storage)
{
    private const string ImageType = "image/jpeg";
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("synthetic image bytes");
    [Fact]
    public async Task CreateUploadUrl_MatchingPut_StoresWhatVerificationReports()
    {
        var key = UploadKey();
        var upload = await storage.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length);

        var response = await PutAsync(upload.Url, Bytes, ImageType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await storage.ApiStore.GetPrivateObjectAsync(key, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(Bytes.Length, stored.Length);
        Assert.Equal(ImageType, stored.ContentType);
        Assert.Equal(response.Headers.ETag?.Tag.Trim('"'), stored.ETag);
    }

    [Fact]
    public async Task CreateUploadUrl_DifferentContentType_IsRejected()
    {
        var key = UploadKey();
        var upload = await storage.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length);

        var response = await PutAsync(upload.Url, Bytes, "image/png");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await storage.ApiStore.GetPrivateObjectAsync(key, CancellationToken.None));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task CreateUploadUrl_DifferentLength_IsRejectedWithoutStoring(int lengthDifference)
    {
        var key = UploadKey();
        var upload = await storage.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length);

        var response = await PutAsync(upload.Url, new byte[Bytes.Length + lengthDifference], ImageType);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await storage.ApiStore.GetPrivateObjectAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task GetPrivateObject_MissingKey_ReturnsNull() =>
        Assert.Null(await storage.ApiStore.GetPrivateObjectAsync(UploadKey(), CancellationToken.None));

    [Fact]
    public async Task CreateDownloadUrl_StoredObject_DownloadsAsNamedAttachment()
    {
        var key = await UploadAsync();
        var download = await storage.ApiStore.CreateDownloadUrlAsync(key, "Session summary.pdf");

        var response = await storage.Browser.GetAsync(download.Url);

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
        await storage.MediaWorkerStore.CopyPrivateToPublicAsync(
            await UploadAsync(), publicKey, ImageType, CancellationToken.None);

        var response = await storage.Browser.GetAsync(storage.MediaWorkerStore.PublicUrl(publicKey));

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
        await storage.MediaWorkerStore.PutPublicAsync(key, published, ImageType, CancellationToken.None);
        var replacement = UploadKey();
        var upload = await storage.ApiStore.CreateUploadUrlAsync(replacement, ImageType, 11);
        (await PutAsync(upload.Url, Encoding.UTF8.GetBytes("replacement"), ImageType)).EnsureSuccessStatusCode();

        var copied = await storage.MediaWorkerStore.CopyPrivateToPublicAsync(
            replacement, key, ImageType, CancellationToken.None);

        Assert.False(copied);
        Assert.Equal(Bytes, await storage.Browser.GetByteArrayAsync(storage.MediaWorkerStore.PublicUrl(key)));
    }

    [Fact]
    public async Task PutPublic_ExistingKey_KeepsPublishedBytes()
    {
        var key = PublicKey();
        using var first = new MemoryStream(Bytes);
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("replacement"));

        Assert.True(await storage.MediaWorkerStore.PutPublicAsync(key, first, ImageType, CancellationToken.None));
        Assert.False(await storage.MediaWorkerStore.PutPublicAsync(key, second, ImageType, CancellationToken.None));

        Assert.Equal(Bytes, await storage.Browser.GetByteArrayAsync(storage.MediaWorkerStore.PublicUrl(key)));
    }

    [Fact]
    public async Task AnonymousAccess_PrivateObjectAndPublicListing_AreDenied()
    {
        var key = await UploadAsync();

        var privateRead = await storage.Browser.GetAsync(
            new Uri(storage.ServiceUrl, $"{ProvisionedStorageFixture.PrivateBucket}/{key}"));
        var publicListing = await storage.Browser.GetAsync(
            new Uri(storage.ServiceUrl, $"{ProvisionedStorageFixture.PublicBucket}?list-type=2"));

        Assert.Equal(HttpStatusCode.Forbidden, privateRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publicListing.StatusCode);
    }

    [Fact]
    public async Task ApiFilesUser_PublicWriteAndListingOutsideUploads_AreDenied()
    {
        var publicWrite = await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.ApiClient.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = ProvisionedStorageFixture.PublicBucket,
                Key = PublicKey(),
                ContentBody = "denied"
            }));
        var listing = await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.ApiClient.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = ProvisionedStorageFixture.PrivateBucket }));

        Assert.Equal(HttpStatusCode.Forbidden, publicWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listing.StatusCode);
    }

    // Listing under uploads/ lets stores that follow AWS report a missing upload as 404 rather than 403.
    [Fact]
    public async Task ApiFilesUser_ListingUploads_IsAllowed()
    {
        var key = await UploadAsync();

        var listing = await storage.ApiClient.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = ProvisionedStorageFixture.PrivateBucket,
            Prefix = key
        });

        Assert.Equal(key, Assert.Single(listing.S3Objects ?? []).Key);
    }

    [Theory]
    [InlineData(ProvisionedStorageFixture.WebOrigin, true)]
    [InlineData("http://elsewhere.example", false)]
    public async Task UploadPreflight_Origin_IsAllowedOnlyForTheApplication(string origin, bool allowed)
    {
        using var preflight = new HttpRequestMessage(
            HttpMethod.Options,
            new Uri(storage.ServiceUrl, $"{ProvisionedStorageFixture.PrivateBucket}/{UploadKey()}"));
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await storage.Browser.SendAsync(preflight);

        var allowedOrigin = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.Single()
            : null;
        Assert.Equal(allowed ? origin : null, allowedOrigin);
    }

    [Fact]
    public async Task PrivateBucketLifecycle_ExpiresOnlyUnverifiedUploads()
    {
        var lifecycle = await storage.RootClient.GetLifecycleConfigurationAsync(
            ProvisionedStorageFixture.PrivateBucket);

        var rule = Assert.Single(lifecycle.Configuration.Rules);
        Assert.Equal(LifecycleRuleStatus.Enabled, rule.Status);
        Assert.Equal(
            ObjectStore.UploadPrefix,
            rule.Filter.LifecycleFilterPredicate switch
            {
                LifecyclePrefixPredicate prefix => prefix.Prefix,
                _ => rule.Filter.Prefix
            });
        Assert.Equal(ProvisionedStorageFixture.UploadExpiryDays, rule.Expiration.Days);
    }

    private static string UploadKey() => $"{ObjectStore.UploadPrefix}{Guid.NewGuid():N}";

    private static string PublicKey() => $"stations/{Guid.NewGuid():N}/display.webp";

    private async Task<string> UploadAsync()
    {
        var key = UploadKey();
        var upload = await storage.ApiStore.CreateUploadUrlAsync(key, ImageType, Bytes.Length);
        (await PutAsync(upload.Url, Bytes, ImageType)).EnsureSuccessStatusCode();
        return key;
    }

    private async Task<HttpResponseMessage> PutAsync(Uri url, byte[] body, string contentType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await storage.Browser.PutAsync(url, content);
    }
}
