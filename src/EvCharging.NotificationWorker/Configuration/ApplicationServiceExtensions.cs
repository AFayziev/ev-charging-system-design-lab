using Amazon.Runtime;
using EvCharging.Domain.Identity;
using EvCharging.NotificationWorker.Delivery;
using EvCharging.NotificationWorker.Templates.Reservation;
using Microsoft.Extensions.Options;
using Wolverine;

namespace EvCharging.NotificationWorker.Configuration;

internal static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ReservationEmailRenderer>();
        services.AddOptions<NotificationDeliveryOptions>()
            .BindConfiguration("NotificationDelivery")
            .Validate(options => Enum.IsDefined(options.Provider), "Configure a supported notification provider.")
            .Validate(options => EmailAddress.Create(options.SenderEmail).IsSuccess, "Configure a valid sender email.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.SenderName) && options.SenderName.Length <= 100,
                "Configure a sender name of at most 100 characters.")
            .Validate(
                options => options.Provider != NotificationDeliveryProvider.AmazonSes
                    || FallbackRegionFactory.GetRegionEndpoint() is not null,
                "Amazon SES delivery requires an AWS region (AWS_REGION).")
            .Validate(options => options.MaximumConcurrentSends is > 0 and <= 32, "Concurrent sends must be 1 to 32.")
            .Validate(
                options => options.RetryDelays is { Length: <= 4 }
                    && options.RetryDelays.All(delay => delay > TimeSpan.Zero && delay <= TimeSpan.FromMinutes(5)),
                "Configure up to four positive retry delays, each at most five minutes.")
            .ValidateOnStart();
        services.AddHttpClient<MailpitNotificationDelivery>(
            client => client.BaseAddress = new Uri("http://notification-mail/"))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false
            });
        services.AddHttpClient(AmazonSesClient.HttpClientName);
        services.AddSingleton(AmazonSesClient.Create);
        services.AddSingleton<AmazonSesNotificationDelivery>();
        services.AddTransient<INotificationDelivery>(provider =>
            provider.GetRequiredService<IOptions<NotificationDeliveryOptions>>().Value.Provider switch
            {
                NotificationDeliveryProvider.AmazonSes => provider.GetRequiredService<AmazonSesNotificationDelivery>(),
                _ => provider.GetRequiredService<MailpitNotificationDelivery>()
            });
        services.AddOptions<MessagingFailureOptions>()
            .BindConfiguration("MessagingFailures")
            .Validate(
                options => options.TransientDatabaseRetryDelays is { Length: > 0 and <= 5 }
                    && options.TransientDatabaseRetryDelays.All(delay => delay > TimeSpan.Zero
                        && delay <= TimeSpan.FromMinutes(1)),
                "Configure one to five positive database retry delays, each at most one minute.")
            .ValidateOnStart();
        services.AddOptions<NotificationPreparationOptions>()
            .BindConfiguration("NotificationPreparation")
            .Validate(
                options => options.MaximumConcurrentPreparations is > 0 and <= 32,
                "Concurrent notification preparations must be 1 to 32.")
            .ValidateOnStart();
        services.AddSingleton<IWolverineExtension, NotificationMessagingExtension>();
        return services;
    }
}
