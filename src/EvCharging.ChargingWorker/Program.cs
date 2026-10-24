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
using Wolverine;
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
builder.AddObservationStreamConsumer<SessionEvidenceBatchHandler>();
builder.AddObservationStreamProducer();
// Wolverine is configured before the options pipeline runs; the pipeline validates these sections at startup.
var chargingExecutionOptions = builder.Configuration.GetRequiredSection(ChargingExecutionOptions.SectionName)
    .Get<ChargingExecutionOptions>()!;
var reservationExpiryOptions = builder.Configuration.GetRequiredSection(ReservationExpiryOptions.SectionName)
    .Get<ReservationExpiryOptions>()!;
var sessionEvidenceRefreshOptions = builder.Configuration.GetRequiredSection(SessionEvidenceRefreshOptions.SectionName)
    .Get<SessionEvidenceRefreshOptions>()!;
var messagingFailureOptions = builder.Configuration.GetRequiredSection(MessagingFailureOptions.SectionName)
    .Get<MessagingFailureOptions>()!;
builder.AddMessaging(
    options =>
    {
        options.AddMessageStorage(builder.Configuration, "charging");
        options.UseEntityFrameworkCoreTransactions();
        options.CodeGeneration.AlwaysUseServiceLocationFor<ChargingProviderClientFactory>();
        options.AddReservationEventRouting();
        options.AddChargingCommandRouting();
        options.AddSessionEventRouting();
        options.Policies.LogMessageStarting(LogLevel.Information);
        options.Policies.ForMessagesOfType<ReservationCreatedEvent>().Audit(message => message.ReservationId);
        options.Policies.ForMessagesOfType<ExpireReservationMessage>().Audit(message => message.ReservationId);
        options.Policies.ForMessagesOfType<StartChargingMessage>().Audit(message => message.OperationId);
        options.Policies.ForMessagesOfType<CheckChargingStartMessage>().Audit(message => message.OperationId);
        options.Policies.ForMessagesOfType<StopChargingMessage>().Audit(message => message.OperationId);
        options.Policies.ForMessagesOfType<CheckChargingStopMessage>().Audit(message => message.OperationId);
        options.Policies.ForMessagesOfType<CompleteChargingSessionMessage>().Audit(message => message.SessionId);
        options.Policies.ForMessagesOfType<RefreshSessionEvidenceMessage>().Audit(message => message.SessionId);
        options.Policies.OnException(ExceptionExtensions.IsTransientDatabaseFailure)
            .ScheduleRetry(messagingFailureOptions.TransientDatabaseRetryDelays);
        options.Policies
            .OnException(
                exception => exception is not OperationCanceledException && !exception.IsTransientDatabaseFailure())
            .MoveToErrorQueue();
        options.Policies.UseDurableLocalQueues();
        options.LocalQueueFor<ExpireReservationMessage>()
            .MaximumParallelMessages(reservationExpiryOptions.MaximumConcurrentChecks);
        options.LocalQueueFor<FindOverdueReservationsMessage>().Sequential();
        options.LocalQueueFor<FindUnreconciledChargingMessage>().Sequential();
        options.LocalQueueFor<FindStaleSessionsMessage>().Sequential();
        options.LocalQueueFor<RefreshSessionEvidenceMessage>()
            .MaximumParallelMessages(sessionEvidenceRefreshOptions.MaximumConcurrentSnapshotReads);
        options.Schedules.ScheduleRecurring<FindOverdueReservationsMessage>(
            ParseCronSchedule(
                $"{ReservationExpiryOptions.SectionName}:{nameof(ReservationExpiryOptions.ScanSchedule)}",
                reservationExpiryOptions.ScanSchedule));
        options.Schedules.ScheduleRecurring<FindUnreconciledChargingMessage>(
            ParseCronSchedule(
                $"{ChargingExecutionOptions.SectionName}:"
                + nameof(ChargingExecutionOptions.ContinuedStatusCheckSchedule),
                chargingExecutionOptions.ContinuedStatusCheckSchedule));
        options.Schedules.ScheduleRecurring<FindStaleSessionsMessage>(
            ParseCronSchedule(
                $"{SessionEvidenceRefreshOptions.SectionName}:{nameof(SessionEvidenceRefreshOptions.ScanSchedule)}",
                sessionEvidenceRefreshOptions.ScanSchedule));
        options.ListenToRabbitQueue(ReservationEventTopology.ChargingQueue)
            .MaximumParallelMessages(reservationExpiryOptions.MaximumConcurrentChecks);
        foreach (string queue in ChargingCommandTopology.ListenerQueues)
        {
            options.ListenToRabbitQueue(queue)
                .MaximumParallelMessages(chargingExecutionOptions.MaximumConcurrentCommands);
        }
    });
builder.Services.AddApplicationServices();
builder.Services.AddHttpClient("gateway", client => client.BaseAddress = new("https+http://gateway/api/"));
builder.Services.AddHttpClient(
    "amptrail-provider-api",
    client => client.BaseAddress = new("https+http://amptrail-provider-api/"));

using var host = builder.Build();

await host.RunAsync();

static CronSchedule ParseCronSchedule(string setting, string expression) =>
    CronSchedule.TryParse(expression, TimeZoneInfo.Utc, out var schedule)
        ? schedule
        : throw new InvalidOperationException(
            $"{setting} must be a cron expression no more frequent than every "
            + $"{CronSchedule.MinimumCadence.TotalSeconds:0} seconds.");
