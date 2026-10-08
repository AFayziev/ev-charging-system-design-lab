using Microsoft.AspNetCore.Authentication;

namespace EvCharging.Gateway.Authentication;

public sealed record SessionRefreshResult(SessionRefreshStatus Status, AuthenticationTicket? Ticket);
