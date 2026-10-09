using System.Text.Json;

namespace EvCharging.Contracts;

public static class PlatformRoleExtensions
{
    public static string ToClaimValue(this PlatformRole role) =>
        JsonNamingPolicy.CamelCase.ConvertName(role.ToString());
}
