namespace EvCharging.AppHost;

/// <summary>
/// Provisioned object storage: the endpoint hosts call, the browser-facing address, and the scoped user of
/// each host.
/// </summary>
internal sealed record ObjectStoreAccess(
    IResourceBuilder<ContainerResource> Provisioning,
    EndpointReference Endpoint,
    ReferenceExpression PublicServiceUrl,
    ObjectStoreUser ApiFiles,
    ObjectStoreUser MediaWorker)
{
    public const string PrivateBucket = "private-files";
    public const string PublicBucket = "public-media";
}
