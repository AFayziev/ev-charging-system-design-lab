using Microsoft.AspNetCore.Authentication;

namespace EvCharging.Gateway.Authentication;

/// <summary>
/// A ticket records a confirmed refresh. A null ticket asserts that the provider made no token change.
/// Exceptions, including cancellation, represent an uncertain effect and invalidate the session.
/// </summary>
public sealed record TokenRefreshResponse(AuthenticationTicket? Ticket);
