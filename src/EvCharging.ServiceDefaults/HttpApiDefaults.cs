using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Microsoft.Extensions.Hosting;

public static class HttpApiDefaultsExtensions
{
    public static WebApplicationBuilder AddHttpApiDefaults(this WebApplicationBuilder builder,
        string openApiBasePath = "/")
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Servers = [new OpenApiServer { Url = openApiBasePath }];
            return Task.CompletedTask;
        }));
        builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(10)
        });
        return builder;
    }

    public static WebApplication UseHttpApiDefaults(this WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode : StatusCodes.Status500InternalServerError
        });
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseRequestTimeouts();
        return app;
    }

    public static WebApplication MapHttpApiDiagnostics(this WebApplication app, string host)
    {
        app.MapDefaultEndpoints();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapGet("/diagnostics/instance", () => new
            {
                host,
                instance = $"{Environment.MachineName}:{Environment.ProcessId}",
                stub = true
            });
        }
        return app;
    }
}
