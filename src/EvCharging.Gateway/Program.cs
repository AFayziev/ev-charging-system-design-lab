using EvCharging.Gateway.Authentication;
using EvCharging.Gateway.Diagnostics;
using EvCharging.Gateway.Persistence;
using EvCharging.Gateway.Proxy;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddAuthenticationPersistence();
builder.AddAuthenticationSessions();
builder.AddBrowserAuthentication();
builder.AddHttpApiDefaults();
builder.AddFrontendDiagnostics();
builder.AddGatewayReverseProxy();

var app = builder.Build();

app.UseHttpApiDefaults();

if (app.Services.GetRequiredService<IOptions<FrontendDiagnosticsOptions>>().Value.Enabled)
{
    app.UseSentryTunneling("/diagnostics/sentry");
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Gateway-Instance"] = $"{Environment.MachineName}:{Environment.ProcessId}";
        await next(context);
    });
}

app.MapHttpApiDiagnostics("gateway");
app.MapBrowserAuthentication();
app.MapFrontendDiagnostics();
app.MapReverseProxy();

app.Run();
