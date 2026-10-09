using Microsoft.Extensions.Options;

namespace EvCharging.Gateway.Diagnostics;

internal static class FrontendDiagnosticsExtensions
{
    public static IHostApplicationBuilder AddFrontendDiagnostics(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<FrontendDiagnosticsOptions>()
            .Bind(builder.Configuration.GetSection("FrontendDiagnostics"))
            .ValidateDataAnnotations()
            .Validate(
                options => !options.Enabled || (Uri.TryCreate(options.Dsn, UriKind.Absolute, out var dsn)
                    && dsn.Scheme == "https" && dsn.Host.EndsWith(".sentry.io", StringComparison.OrdinalIgnoreCase)
                    && dsn.UserInfo.Length > 0 && long.TryParse(dsn.AbsolutePath.Trim('/'), out _)
                    && dsn.Query.Length == 0 && dsn.Fragment.Length == 0
                    && !string.IsNullOrWhiteSpace(options.Environment) && !string.IsNullOrWhiteSpace(options.Release)),
                "Enabled frontend diagnostics require a hosted Sentry DSN, environment and release.")
            .ValidateOnStart();
        builder.Services.AddSentryTunneling();
        return builder;
    }

    public static IEndpointRouteBuilder MapFrontendDiagnostics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/diagnostics/config",
            (HttpContext context, IOptions<FrontendDiagnosticsOptions> options) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var settings = options.Value;
                return TypedResults.Ok(new FrontendDiagnosticsConfigResponse(
                    settings.Enabled,
                    settings.Dsn,
                    settings.Environment,
                    settings.Release,
                    settings.ErrorSampleRate,
                    settings.TraceSampleRate,
                    settings.LogLevels,
                    settings.ReplayEnabled));
            });
        return endpoints;
    }
}
