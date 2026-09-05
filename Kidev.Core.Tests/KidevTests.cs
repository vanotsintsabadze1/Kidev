using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kidev.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kidev.Core.Tests;

/// <summary>
/// Tests build-time Kidev job registration.
/// </summary>
public sealed class KidevTests
{
    /// <summary>
    /// Verifies constant arguments and minute schedules are retained in the registration catalog.
    /// </summary>
    [Fact]
    public void RunWithConstantArgumentsRetainsJobMetadata()
    {
        var kidev = new Kidev();

        kidev.Run<IJobService>("send-digest", service => service.SendDigest("weekly", 25))
            .EveryMinute(5);

        KidevRegistrationCatalog catalog = kidev.Freeze();
        Data.JobDefinition jobDefinition = catalog.JobDefinitions.Should().ContainSingle().Subject;

        jobDefinition.RegistrationKey.Should().Be("send-digest");
        jobDefinition.MethodName.Should().Be(nameof(IJobService.SendDigest));
        jobDefinition.CronExpression.Should().Be("*/5 * * * *");
        jobDefinition.ArgumentsJson.Should().Be("[\"weekly\",25]");
        jobDefinition.MethodParameterTypesJson.Should().Contain("System.String");
        jobDefinition.MethodParameterTypesJson.Should().Contain("System.Int32");
    }

    /// <summary>
    /// Verifies captured values are rejected until a broader serialization contract exists.
    /// </summary>
    [Fact]
    public void RunWithCapturedArgumentThrowsArgumentException()
    {
        var kidev = new Kidev();
        string frequency = "weekly";

        Action action = () => kidev.Run<IJobService>("send-digest", service => service.SendDigest(frequency, 25));

        action.Should().Throw<ArgumentException>()
            .WithMessage("Only constant method arguments are supported.*");
    }

    /// <summary>
    /// Verifies the runner invokes the next due job using its stored method signature and arguments.
    /// </summary>
    [Fact]
    public async Task RunnerExecutesNextDueJob()
    {
        var executionCompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobCompletionSource = new TaskCompletionSource<(DateTimeOffset LastExecutedAtUtc, DateTimeOffset NextExecutionAtUtc)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobDefinition = new JobDefinition
        {
            RegistrationKey = "execute-report",
            ServiceTypeName = typeof(IRunnerJobService).AssemblyQualifiedName!,
            MethodName = nameof(IRunnerJobService.Execute),
            MethodParameterTypesJson = $"[\"{typeof(string).AssemblyQualifiedName}\"]",
            ArgumentsJson = "[\"daily\"]",
            CronExpression = "*/1 * * * *",
            NextExecutionAtUtc = DateTimeOffset.UtcNow
        };
        var services = new ServiceCollection();
        services.AddScoped<IRunnerJobService>(_ => new RunnerJobService(executionCompletionSource));
        services.AddSingleton<IJobDefinitionStore>(new TestJobDefinitionStore(jobDefinition, jobCompletionSource));

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        KidevRegistrationCatalog registrationCatalog = new Kidev().Freeze();
        using var runner = new KidevRunner(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            registrationCatalog,
            NullLogger<KidevRunner>.Instance);

        await runner.StartAsync(CancellationToken.None);

        string executionArgument = await executionCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (DateTimeOffset lastExecutedAtUtc, DateTimeOffset nextExecutionAtUtc) = await jobCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runner.StopAsync(CancellationToken.None);

        executionArgument.Should().Be("daily");
        nextExecutionAtUtc.Should().BeAfter(lastExecutedAtUtc);
        ((TestJobDefinitionStore)serviceProvider.GetRequiredService<IJobDefinitionStore>()).WasSynchronized.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the runner records invocation failures and advances the schedule.
    /// </summary>
    [Fact]
    public async Task RunnerRecordsFailedJobExecution()
    {
        var failureCompletionSource = new TaskCompletionSource<(string ErrorType, string ErrorMessage)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobDefinition = new JobDefinition
        {
            RegistrationKey = "failing-job",
            ServiceTypeName = typeof(IFailingRunnerJobService).AssemblyQualifiedName!,
            MethodName = nameof(IFailingRunnerJobService.Execute),
            MethodParameterTypesJson = "[]",
            ArgumentsJson = "[]",
            CronExpression = "*/1 * * * *",
            NextExecutionAtUtc = DateTimeOffset.UtcNow
        };
        var services = new ServiceCollection();
        services.AddScoped<IFailingRunnerJobService>(_ => new FailingRunnerJobService());
        services.AddSingleton<IJobDefinitionStore>(new TestJobDefinitionStore(jobDefinition, null, failureCompletionSource));

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        using var runner = new KidevRunner(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new Kidev().Freeze(),
            NullLogger<KidevRunner>.Instance);

        await runner.StartAsync(CancellationToken.None);

        (string errorType, string errorMessage) = await failureCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runner.StopAsync(CancellationToken.None);

        errorType.Should().Be(typeof(InvalidOperationException).FullName);
        errorMessage.Should().Be("Expected job failure.");
    }

    /// <summary>
    /// Defines a service shape used to inspect a registered method call.
    /// </summary>
    private interface IJobService
    {
        /// <summary>
        /// Sends a digest with a supplied frequency and maximum item count.
        /// </summary>
        /// <param name="frequency">The digest frequency.</param>
        /// <param name="maximumItems">The maximum number of items to include.</param>
        void SendDigest(string frequency, int maximumItems);
    }

    /// <summary>
    /// Defines a service shape used to execute a persisted job definition.
    /// </summary>
    private interface IRunnerJobService
    {
        /// <summary>
        /// Executes the persisted job payload.
        /// </summary>
        /// <param name="schedule">The persisted schedule argument.</param>
        void Execute(string schedule);
    }

    /// <summary>
    /// Defines a job service that fails while executing.
    /// </summary>
    private interface IFailingRunnerJobService
    {
        /// <summary>Throws an expected test exception.</summary>
        void Execute();
    }

    private sealed class RunnerJobService(TaskCompletionSource<string> completionSource) : IRunnerJobService
    {
        public void Execute(string schedule)
        {
            completionSource.SetResult(schedule);
        }
    }

    private sealed class FailingRunnerJobService : IFailingRunnerJobService
    {
        public void Execute()
        {
            throw new InvalidOperationException("Expected job failure.");
        }
    }

    private sealed class TestJobDefinitionStore(
        JobDefinition jobDefinition,
        TaskCompletionSource<(DateTimeOffset LastExecutedAtUtc, DateTimeOffset NextExecutionAtUtc)>? completionSource,
        TaskCompletionSource<(string ErrorType, string ErrorMessage)>? failureCompletionSource = null) : IJobDefinitionStore
    {
        private JobDefinition? nextJobDefinition = jobDefinition;
        private readonly Guid claimId = Guid.NewGuid();

        public bool WasSynchronized { get; private set; }

        public Task SynchronizeAsync(IReadOnlyList<JobDefinition> jobDefinitions, CancellationToken cancellationToken)
        {
            WasSynchronized = true;
            return Task.CompletedTask;
        }

        public Task<ClaimedJob?> ClaimNextDueAsync(
            string workerId,
            DateTimeOffset utcNow,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            if (!WasSynchronized)
            {
                throw new InvalidOperationException("The runner queried jobs before synchronization completed.");
            }

            JobDefinition? result = nextJobDefinition;
            nextJobDefinition = null;
            return Task.FromResult(result is null ? null : new ClaimedJob(result, claimId));
        }

        public Task<bool> RenewLeaseAsync(
            int jobId,
            Guid claimId,
            DateTimeOffset leaseExpiresAtUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task CompleteAsync(
            int jobId,
            Guid claimId,
            DateTimeOffset lastExecutedAtUtc,
            DateTimeOffset nextExecutionAtUtc,
            CancellationToken cancellationToken)
        {
            completionSource?.SetResult((lastExecutedAtUtc, nextExecutionAtUtc));
            return Task.CompletedTask;
        }

        public Task FailAsync(int jobId, Guid claimId, DateTimeOffset completedAtUtc, DateTimeOffset nextExecutionAtUtc, string errorType, string errorMessage, CancellationToken cancellationToken)
        {
            failureCompletionSource?.SetResult((errorType, errorMessage));
            return Task.CompletedTask;
        }

        public Task ExpireLeasesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteExecutionHistoryAsync(DateTimeOffset completedBeforeUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
