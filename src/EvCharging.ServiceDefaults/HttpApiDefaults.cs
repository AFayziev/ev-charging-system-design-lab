using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Microsoft.Extensions.Hosting;

public static class HttpApiDefaultsExtensions
{
    public static WebApplicationBuilder AddHttpApiDefaults(
        this WebApplicationBuilder builder,
        string openApiBasePath = "/")
    {
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Servers = [new OpenApiServer { Url = openApiBasePath }];
            return Task.CompletedTask;
        }));
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
        return app;
    }

    public static WebApplication MapHttpApiDiagnostics(this WebApplication app, string host)
    {
        app.MapDefaultEndpoints();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference(options => options.DisableAgent().AddServer("/")).AllowAnonymous();
            app.MapGet(
                "/diagnostics/instance",
                () => TypedResults.Ok(new InstanceResponse(
                    host, $"{Environment.MachineName}:{Environment.ProcessId}")));
        }

        return app;
    }
}
