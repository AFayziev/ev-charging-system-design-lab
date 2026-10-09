namespace EvCharging.Gateway.Authentication;

internal sealed record BrowserSessionResponse(
    bool SignedIn,
    BrowserSessionDisplay? Display,
    string? AntiforgeryToken);

internal sealed record BrowserSessionDisplay(string? Name, string? Username, string? Email);
