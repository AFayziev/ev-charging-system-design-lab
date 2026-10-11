using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace EvCharging.Api.Authentication;

internal sealed record UserIdentity(string Issuer, string Subject);

internal static class UserIdentityExtensions
{
    public static UserIdentity? GetUserIdentity(this ClaimsPrincipal user)
    {
        string? issuer = user.FindFirst(JwtRegisteredClaimNames.Iss)?.Value;
        string? subject = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return user.Identity?.IsAuthenticated == true
            && !string.IsNullOrWhiteSpace(issuer) && !string.IsNullOrWhiteSpace(subject)
            ? new UserIdentity(issuer, subject)
            : null;
    }
}
