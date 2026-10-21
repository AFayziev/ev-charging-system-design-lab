using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Authentication;

/// <summary>The configured source keys, hashed once so a request only hashes the key it presents.</summary>
internal sealed class ObservationSourceKeyring(IOptions<ObservationSourceOptions> sources)
{
    private readonly (string IntegrationKey, string Client, byte[] Hash)[] _keys = sources.Value.Sources
        .SelectMany(source => source.Value.ApiKeys.Select(
            apiKey => (source.Key, source.Value.Client, SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)))))
        .ToArray();

    public (string IntegrationKey, string Client)? Find(string apiKey)
    {
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        (string IntegrationKey, string Client)? match = null;

        // Compare every configured key so timing does not reveal which one matched.
        foreach (var (integrationKey, client, hash) in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(presented, hash))
            {
                match = (integrationKey, client);
            }
        }

        return match;
    }
}
