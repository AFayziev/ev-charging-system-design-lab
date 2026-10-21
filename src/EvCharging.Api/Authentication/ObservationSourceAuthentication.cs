using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EvCharging.Api.Authentication;

internal static class ObservationSourceAuthentication
{
    public const string Scheme = "ObservationSource";
    public const string ApiKeyHeader = "X-Api-Key";
    public const string IntegrationKeyClaim = "integration_key";
    public const string ClientClaim = "observation_client";
    public const string WattHarborPolicy = "observation-source-wattharbor";
    public const string WattHarborClient = "WattHarbor";

    public static IHostApplicationBuilder AddObservationSourceAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<ObservationSourceOptions>()
            .BindConfiguration(ObservationSourceOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.Sources.Values.All(source => source.Client.Length > 0
                    && source.ApiKeys.Length > 0 && source.ApiKeys.All(key => !string.IsNullOrWhiteSpace(key))),
                "Each observation source requires a client and non-blank API keys.")
            .ValidateOnStart();

        // ASP.NET Core ships no API-key scheme; providers authenticate callbacks with a shared key.
        builder.Services.AddSingleton<ObservationSourceKeyring>();
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ObservationSourceKeyHandler>(
            Scheme, null);
        builder.Services.AddAuthorizationBuilder().AddPolicy(
            WattHarborPolicy,
            policy => policy.AddAuthenticationSchemes(Scheme)
                .RequireClaim(IntegrationKeyClaim)
                .RequireClaim(ClientClaim, WattHarborClient));
        return builder;
    }
}

internal sealed class ObservationSourceKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ObservationSourceKeyring keyring)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ObservationSourceAuthentication.ApiKeyHeader, out var values)
            || values.Count != 1 || string.IsNullOrEmpty(values[0]))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (keyring.Find(values[0]!) is not { } source)
        {
            return Task.FromResult(AuthenticateResult.Fail("The API key is not recognized."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ObservationSourceAuthentication.IntegrationKeyClaim, source.IntegrationKey),
                new Claim(ObservationSourceAuthentication.ClientClaim, source.Client)
            ],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
