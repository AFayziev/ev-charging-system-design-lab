namespace EvCharging.AppHost;

/// <summary>A host's storage user, limited by its policy in infra/rustfs/platform.</summary>
internal sealed record ObjectStoreUser(string AccessKey, IResourceBuilder<ParameterResource> SecretKey);
