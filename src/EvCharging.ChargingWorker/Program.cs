using EvCharging.ChargingWorker.Configuration;
using EvCharging.ChargingWorker.Handlers;
using EvCharging.ChargingWorker.Observations;
using EvCharging.ChargingWorker.Persistence;
using EvCharging.ChargingWorker.Providers;
using EvCharging.Contracts.Charging;
using EvCharging.Contracts.Events.Reservations;
using EvCharging.Contracts.Observations;
using EvCharging.Messaging;
using EvCharging.ObservationStream;
using EvCharging.Persistence.Extensions;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddChargingProviders();
builder.AddPlatformPersistence();
builder.Services.AddOptions<ObservationClockSkewOptions>()
    .BindConfiguration(ObservationClockSkewOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.AddObservationStreamConsumer<SessionEvidenceRecorder>();
int maximumConcurrentCommands = builder.Configuration.GetValue<int>("ChargingExecution:MaximumConcurrentCommands");
int maximumConcurrentChecks = builder.Configuration.GetValue<int>("ReservationExpiry:MaximumConcurrentChecks");
var retryDelays = builder.Configuration.GetSection("MessagingFailures:TransientDatabaseRetryDelays")
    .Get<TimeSpan[]>() ?? throw new InvalidOperationException("Database message retry delays are required.");
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "charging");
        options.UseEntityFrameworkCoreTransactions();
        options.CodeGeneration.AlwaysUseServiceLocationFor<ChargingProviderClientFactory>();
        options.AddReservationEventRouting();
        options.AddChargingCommandRouting();
        options.Policies.LogMessageStarting(LogLevel.Information);
        options.Policies.ForMessagesOfType<ReservationCreated>().Audit(message => message.ReservationId);
        options.Policies.ForMessagesOfType<ExpireReservation>().Audit(message => message.ReservationId);
        options.Policies.ForMessagesOfType<StartCharging>().Audit(message => message.OperationId);
        options.Policies.ForMessagesOfType<CheckChargingStart>().Audit(message => message.OperationId);
        options.Policies.ForMessagesOfType<StopCharging>().Audit(message => message.OperationId);
        options.Policies.ForMessagesOfType<CheckChargingStop>().Audit(message => message.OperationId);
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
        foreach (string queue in ChargingCommandTopology.ListenerQueues)
        {
            options.ListenToRabbitQueue(queue).MaximumParallelMessages(maximumConcurrentCommands);
        }
    });
builder.Services.AddApplicationServices();
builder.Services.AddHttpClient("gateway", client => client.BaseAddress = new("https+http://gateway/api/"));
builder.Services.AddHttpClient(
    "amptrail-provider-api",
    client => client.BaseAddress = new("https+http://amptrail-provider-api/"));

using var host = builder.Build();

await host.RunAsync();
