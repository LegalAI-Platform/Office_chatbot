using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Office.Core.WhatsApp;
using Office.Infrastructure.WhatsApp;

namespace Office.Api.WhatsApp;

/// <summary>Hangfire entry points. They only call the Core use cases.</summary>
public sealed class WhatsAppJobs(EventProcessor processor, EchoResponder echo, MetaAdmin admin, ILogger<WhatsAppJobs> logger)
{
    public const string TemplatesSyncJobId = "whatsapp-templates-sync";
    public const string EchoTestJobId = "whatsapp-echo-test";

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 60, 300], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [AlertOnFinalFailure]
    public async Task ProcessEventAsync(Guid eventId, CancellationToken ct)
    {
        var result = await processor.ProcessAsync(eventId, ct);
        if (result is not null)
        {
            logger.LogInformation("Event {EventId}: {New} new, {Dup} duplicate, {Group} group, {Statuses} statuses, template {Template}",
                eventId, result.MessagesNew, result.MessagesDuplicate, result.MessagesGroupIgnored, result.StatusesNew, result.TemplateUpdated);
        }
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(60)]
    public async Task EchoTestAsync(CancellationToken ct)
    {
        var replied = await echo.RunAsync(ct);
        if (replied > 0)
        {
            logger.LogInformation("Echo test replied to {Count} message(s)", replied);
        }
    }

    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(600)]
    public async Task SyncTemplatesAsync(CancellationToken ct)
    {
        var report = await admin.SyncTemplatesAsync(ct);
        logger.LogInformation("Templates synced: {Count}", report["synced"]?.GetValue<int>());
    }
}

/// <summary>Sends an ops alert when a job lands in Failed after all retries.</summary>
public sealed class AlertOnFinalFailureAttribute : JobFilterAttribute, IApplyStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is FailedState failed)
        {
            var notifier = Program.Services?.GetService<IOpsNotifier>();
            var job = context.BackgroundJob.Job;
            notifier?.NotifyAsync($"Job {job.Type.Name}.{job.Method.Name} failed after all retries ({failed.Exception.GetType().Name}).", CancellationToken.None)
                .GetAwaiter().GetResult();
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
