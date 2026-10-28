namespace EvCharging.AppHost;

/// <summary>
/// Provisioned object storage: the endpoint hosts call, the browser-facing address, and the scoped user of
/// each host.
/// </summary>
internal sealed record ObjectStorageAccess(
    IResourceBuilder<ContainerResource> Provisioning,
    EndpointReference Endpoint,
    ReferenceExpression PublicServiceUrl,
    ObjectStorageUser ApiFiles,
    ObjectStorageUser MediaWorker)
{
    public const string PrivateBucket = "private-files";
    public const string PublicBucket = "public-media";
}
