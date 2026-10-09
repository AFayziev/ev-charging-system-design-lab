namespace Microsoft.Extensions.Hosting;

internal sealed record InstanceResponse(string Host, string Instance, bool Stub);
