using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Kidev.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kidev.Core;

/// <summary>
/// Runs core background work for the lifetime of the host.
/// </summary>
internal sealed partial class KidevRunner(
    IServiceScopeFactory serviceScopeFactory,
    KidevRegistrationCatalog registrationCatalog,
    ILogger<KidevRunner> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(1);
    private const int MaximumErrorMessageLength = 4_000;
    private readonly string instanceId = Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (IServiceScope synchronizationScope = serviceScopeFactory.CreateScope())
        {
            IJobDefinitionStore synchronizationStore = synchronizationScope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
            await synchronizationStore.SynchronizeAsync(registrationCatalog.JobDefinitions, stoppingToken);
        }

        var workers = new Task[registrationCatalog.WorkerCount];

        for (int workerIndex = 0; workerIndex < workers.Length; workerIndex++)
        {
            workers[workerIndex] = RunWorkerAsync(workerIndex, stoppingToken);
        }

        Task maintenanceTask = RunMaintenanceAsync(stoppingToken);
        await Task.WhenAll(workers);
        await maintenanceTask;
    }

    private async Task RunWorkerAsync(int workerIndex, CancellationToken stoppingToken)
    {
        string workerId = $"{instanceId}:{workerIndex}";

        while (!stoppingToken.IsCancellationRequested)
        {
            ClaimedJob? claimedJob;

            using (IServiceScope scope = serviceScopeFactory.CreateScope())
            {
                IJobDefinitionStore jobDefinitionStore = scope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
                claimedJob = await jobDefinitionStore.ClaimNextDueAsync(
                    workerId,
                    DateTimeOffset.UtcNow,
                    LeaseDuration,
                    stoppingToken);
            }

            if (claimedJob is null)
            {
                await Task.Delay(PollInterval, stoppingToken);
                continue;
            }

            JobDefinition jobDefinition = claimedJob.JobDefinition;
            LogJobClaimed(logger, jobDefinition.RegistrationKey, workerId, claimedJob.ClaimId);

            using var heartbeatCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            Task<bool> heartbeatTask = MaintainLeaseAsync(claimedJob, workerId, heartbeatCancellationSource.Token);

            try
            {
                LogJobStarted(logger, jobDefinition.RegistrationKey, workerId, claimedJob.ClaimId);
                ExecuteJob(jobDefinition);
            }
            catch (Exception exception)
            {
                Exception jobException = exception is TargetInvocationException { InnerException: not null } invocationException
                    ? invocationException.InnerException
                    : exception;
                heartbeatCancellationSource.Cancel();
                bool failureOwnsClaim = await heartbeatTask;

                if (failureOwnsClaim)
                {
                    DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
                    DateTimeOffset nextExecutionAtUtc = GetNextExecutionAtUtc(jobDefinition, completedAtUtc);

                    using IServiceScope failureScope = serviceScopeFactory.CreateScope();
                    IJobDefinitionStore failureStore = failureScope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
                    await failureStore.FailAsync(
                        jobDefinition.Id,
                        claimedJob.ClaimId,
                        completedAtUtc,
                        nextExecutionAtUtc,
                        jobException.GetType().FullName ?? jobException.GetType().Name,
                        LimitErrorMessage(jobException.Message),
                        stoppingToken);
                }

                LogJobFailed(logger, jobException, jobDefinition.RegistrationKey, workerId, claimedJob.ClaimId);
                continue;
            }
            finally
            {
                heartbeatCancellationSource.Cancel();
                await heartbeatTask;
            }

            bool ownsClaim = await heartbeatTask;

            if (!ownsClaim)
            {
                LogJobCompletionSkipped(logger, jobDefinition.RegistrationKey, workerId, claimedJob.ClaimId);
                continue;
            }

            try
            {
                DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
                DateTimeOffset nextExecutionAtUtc = GetNextExecutionAtUtc(jobDefinition, completedAtUtc);

                using IServiceScope completionScope = serviceScopeFactory.CreateScope();
                IJobDefinitionStore completionStore = completionScope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
                await completionStore.CompleteAsync(
                    jobDefinition.Id,
                    claimedJob.ClaimId,
                    completedAtUtc,
                    nextExecutionAtUtc,
                    stoppingToken);
                LogJobCompleted(logger, jobDefinition.RegistrationKey, workerId, claimedJob.ClaimId);
            }
            catch (Exception exception)
            {
                LogJobFailed(logger, exception, jobDefinition.RegistrationKey, workerId, claimedJob.ClaimId);
            }
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(MaintenanceInterval, stoppingToken);
                DateTimeOffset utcNow = DateTimeOffset.UtcNow;

                using IServiceScope scope = serviceScopeFactory.CreateScope();
                IJobDefinitionStore jobDefinitionStore = scope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
                await jobDefinitionStore.ExpireLeasesAsync(utcNow, stoppingToken);
                await jobDefinitionStore.DeleteExecutionHistoryAsync(
                    utcNow.Subtract(registrationCatalog.ExecutionHistoryRetention),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogMaintenanceFailed(logger, exception);
            }
        }
    }

    private async Task<bool> MaintainLeaseAsync(ClaimedJob claimedJob, string workerId, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(GetHeartbeatInterval(LeaseDuration), cancellationToken);
                DateTimeOffset leaseExpiresAtUtc = DateTimeOffset.UtcNow.Add(LeaseDuration);

                using IServiceScope scope = serviceScopeFactory.CreateScope();
                IJobDefinitionStore jobDefinitionStore = scope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
                bool wasRenewed = await jobDefinitionStore.RenewLeaseAsync(
                    claimedJob.JobDefinition.Id,
                    claimedJob.ClaimId,
                    leaseExpiresAtUtc,
                    cancellationToken);

                if (!wasRenewed)
                {
                    LogClaimLost(logger, workerId, claimedJob.ClaimId, claimedJob.JobDefinition.RegistrationKey);
                    return false;
                }

                LogLeaseRenewed(logger, workerId, claimedJob.ClaimId, claimedJob.JobDefinition.RegistrationKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception exception)
        {
            LogLeaseRenewalFailed(logger, exception, workerId, claimedJob.ClaimId, claimedJob.JobDefinition.RegistrationKey);
            return false;
        }

    }

    private static DateTimeOffset GetNextExecutionAtUtc(JobDefinition jobDefinition, DateTimeOffset completedAtUtc)
    {
        var cronExpression = CronExpression.Parse(jobDefinition.CronExpression);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(jobDefinition.TimeZoneId);
        DateTime nextExecutionAtUtc = cronExpression.GetNextOccurrence(completedAtUtc.UtcDateTime, timeZone)
            ?? throw new InvalidOperationException($"The schedule for job '{jobDefinition.RegistrationKey}' has no future occurrence.");

        return new DateTimeOffset(nextExecutionAtUtc);
    }

    private static TimeSpan GetHeartbeatInterval(TimeSpan leaseDuration)
    {
        var interval = TimeSpan.FromTicks(leaseDuration.Ticks / 3);

        if (interval < TimeSpan.FromSeconds(5))
        {
            return TimeSpan.FromSeconds(5);
        }

        return interval > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : interval;
    }

    private static string LimitErrorMessage(string errorMessage)
    {
        return errorMessage.Length <= MaximumErrorMessageLength
            ? errorMessage
            : errorMessage[..MaximumErrorMessageLength];
    }

    private void ExecuteJob(JobDefinition jobDefinition)
    {
        JobInvocation invocation = InvocationHelper.Create(jobDefinition);

        if (invocation.Arguments.Length != invocation.ParameterTypes.Length)
        {
            throw new InvalidOperationException($"The arguments for job '{jobDefinition.RegistrationKey}' do not match its method signature.");
        }

        object?[] deserializedArguments = new object?[invocation.Arguments.Length];

        for (int index = 0; index < invocation.Arguments.Length; index++)
        {
            deserializedArguments[index] = invocation.Arguments[index].Deserialize(invocation.ParameterTypes[index]);
        }

        using IServiceScope scope = serviceScopeFactory.CreateScope();
        object service = scope.ServiceProvider.GetRequiredService(invocation.ServiceType);
        invocation.Method.Invoke(service, deserializedArguments);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {RegistrationKey} was claimed by worker {WorkerId} with claim {ClaimId}.")]
    private static partial void LogJobClaimed(ILogger logger, string registrationKey, string workerId, Guid claimId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {RegistrationKey} started on worker {WorkerId} with claim {ClaimId}.")]
    private static partial void LogJobStarted(ILogger logger, string registrationKey, string workerId, Guid claimId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {RegistrationKey} finished after worker {WorkerId} lost claim {ClaimId}; completion was not recorded.")]
    private static partial void LogJobCompletionSkipped(ILogger logger, string registrationKey, string workerId, Guid claimId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {RegistrationKey} completed on worker {WorkerId} with claim {ClaimId}.")]
    private static partial void LogJobCompleted(ILogger logger, string registrationKey, string workerId, Guid claimId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {RegistrationKey} failed on worker {WorkerId} with claim {ClaimId}.")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, string registrationKey, string workerId, Guid claimId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker {WorkerId} lost claim {ClaimId} for job {RegistrationKey}.")]
    private static partial void LogClaimLost(ILogger logger, string workerId, Guid claimId, string registrationKey);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Worker {WorkerId} renewed claim {ClaimId} for job {RegistrationKey}.")]
    private static partial void LogLeaseRenewed(ILogger logger, string workerId, Guid claimId, string registrationKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Worker {WorkerId} could not renew claim {ClaimId} for job {RegistrationKey}.")]
    private static partial void LogLeaseRenewalFailed(ILogger logger, Exception exception, string workerId, Guid claimId, string registrationKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job execution maintenance failed.")]
    private static partial void LogMaintenanceFailed(ILogger logger, Exception exception);
}
