using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.RabbitMQ;

namespace Microsoft.Extensions.Hosting;

public static class MessagingExtensions
{
    public static IHostApplicationBuilder AddMessaging(
        this IHostApplicationBuilder builder,
        Action<WolverineOptions> configure)
    {
        builder.UseWolverine(options =>
        {
            options.ApplicationAssembly = Assembly.Load(builder.Environment.ApplicationName);
            options.UseRabbitMqUsingNamedConnection("rabbitmq").AutoProvision();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            configure(options);
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("Wolverine"))
            .WithMetrics(metrics => metrics.AddMeter("Wolverine*"));

        return builder;
    }
}
