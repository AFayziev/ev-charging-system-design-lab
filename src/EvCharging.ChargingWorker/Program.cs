using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Persistence;
using EvCharging.ChargingWorker.Providers.Synchronous;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Messaging;
using EvCharging.Persistence.Extensions;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddSynchronousChargingProvider();
builder.AddPlatformPersistence();
int maximumConcurrentChecks = builder.Configuration.GetValue<int>("ReservationExpiry:MaximumConcurrentChecks");
var retryDelays = builder.Configuration.GetSection("MessagingFailures:TransientDatabaseRetryDelays")
    .Get<TimeSpan[]>() ?? throw new InvalidOperationException("Database message retry delays are required.");
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "charging");
        options.UseEntityFrameworkCoreTransactions();
        options.AddReservationEventRouting();
        options.Policies.LogMessageStarting(LogLevel.Information);
        options.Policies.ForMessagesOfType<ReservationCreated>().Audit(message => message.ReservationId);
        options.Policies.ForMessagesOfType<ExpireReservation>().Audit(message => message.ReservationId);
        options.Policies.OnException(ExceptionExtensions.IsTransientDatabaseFailure).ScheduleRetry(retryDelays);
        options.Policies
            .OnException(
                exception => exception is not OperationCanceledException && !exception.IsTransientDatabaseFailure())
            .MoveToErrorQueue();
        options.Policies.UseDurableLocalQueues();
        options.LocalQueueFor<ExpireReservation>()
            .MaximumParallelMessages(maximumConcurrentChecks);
        options.LocalQueueFor<SweepOverdueReservations>().Sequential();
        options.ListenToRabbitQueue(ReservationEventTopology.ChargingQueue)
            .MaximumParallelMessages(maximumConcurrentChecks);
    });
builder.Services.AddApplicationServices();
builder.Services.AddHttpClient("gateway", client => client.BaseAddress = new("https+http://gateway/api/"));
builder.Services.AddHttpClient(
    "queued-charging-provider-api",
    client => client.BaseAddress = new("https+http://queued-charging-provider-api/"));

using var host = builder.Build();

await host.RunAsync();
