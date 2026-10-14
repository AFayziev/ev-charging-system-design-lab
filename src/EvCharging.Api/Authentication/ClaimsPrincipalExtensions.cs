using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EvCharging.Domain.Identity;

namespace EvCharging.Api.Authentication;

internal static class ClaimsPrincipalExtensions
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

    public static EmailAddress? GetVerifiedEmail(this ClaimsPrincipal user)
    {
        string? email = user.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        bool verified = bool.TryParse(user.FindFirst("email_verified")?.Value, out bool value) && value;
        return verified && email is not null ? EmailAddress.Create(email).ValueOrDefault : null;
    }
}
