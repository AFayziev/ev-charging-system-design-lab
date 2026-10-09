using EvCharging.Contracts;

namespace EvCharging.Gateway.Authentication;

internal sealed record BrowserSessionResponse(
    bool SignedIn,
    BrowserSessionUser? User,
    string? AntiforgeryToken);

internal sealed record BrowserSessionUser(string? Name, string? Username, string? Email, PlatformRole[] Roles);
