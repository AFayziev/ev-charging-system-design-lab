using System.Security.Claims;

namespace EvCharging.Api.Authentication;

public static class RequestUserMiddleware
{
    public static RequestUser Before(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User ?? new ClaimsPrincipal();
        return new RequestUser(principal.GetUserIdentity(), principal);
    }
}
