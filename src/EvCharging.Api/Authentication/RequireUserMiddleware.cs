using System.Security.Claims;
using EvCharging.Api.Errors;
using FluentResults;
using Wolverine;

namespace EvCharging.Api.Authentication;

public static class RequireUserMiddleware<TResponse>
{
    // InvokeAsync captures the matching failure result as its local reply before the chain stops.
    public static (HandlerContinuation, UserIdentity?, ClaimsPrincipal?, OutgoingMessages) Before(RequestUser user) =>
        user.Identity is { } identity
            ? (HandlerContinuation.Continue, identity, user.Principal, [])
            : (HandlerContinuation.Stop, null, null, [Result.Fail<TResponse>(new UnauthenticatedError())]);
}
