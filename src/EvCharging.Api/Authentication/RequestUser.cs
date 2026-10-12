using System.Security.Claims;

namespace EvCharging.Api.Authentication;

public sealed record RequestUser(UserIdentity? Identity, ClaimsPrincipal Principal);
