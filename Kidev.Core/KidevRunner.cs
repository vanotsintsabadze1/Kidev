using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Kidev.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kidev.Core;

/// <summary>
/// Runs core background work for the lifetime of the host.
/// </summary>
internal sealed class KidevRunner(
    IServiceScopeFactory serviceScopeFactory,
    KidevRegistrationCatalog registrationCatalog) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (IServiceScope synchronizationScope = serviceScopeFactory.CreateScope())
        {
            IJobDefinitionStore synchronizationStore = synchronizationScope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
            await synchronizationStore.SynchronizeAsync(registrationCatalog.JobDefinitions, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            JobDefinition? jobDefinition;

            using (IServiceScope scope = serviceScopeFactory.CreateScope())
            {
                IJobDefinitionStore jobDefinitionStore = scope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
                jobDefinition = await jobDefinitionStore.GetNextDueAsync(DateTimeOffset.UtcNow, stoppingToken);
            }

            if (jobDefinition is null)
            {
                await Task.Delay(PollInterval, stoppingToken);
                continue;
            }

            ExecuteJob(jobDefinition);

            DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
            DateTimeOffset nextExecutionAtUtc = GetNextExecutionAtUtc(jobDefinition, completedAtUtc);

            using IServiceScope completionScope = serviceScopeFactory.CreateScope();
            IJobDefinitionStore completionStore = completionScope.ServiceProvider.GetRequiredService<IJobDefinitionStore>();
            await completionStore.CompleteAsync(jobDefinition.Id, completedAtUtc, nextExecutionAtUtc, stoppingToken);
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
}
