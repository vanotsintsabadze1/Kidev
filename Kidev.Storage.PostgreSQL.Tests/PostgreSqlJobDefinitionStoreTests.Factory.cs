using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kidev.Storage.PostgreSQL.Tests;

public sealed partial class PostgreSqlJobDefinitionStoreTests
{
    /// <summary>Verifies registration, monotonic independent heartbeats, terminal stop, and registry retention.</summary>
    [Fact]
    public async Task ProcessLifecycleIsPersistedIndependentlyAsync()
    {
        await using KidevDbContext context = CreateDbContext();
        var store = new PostgreSqlJobDefinitionStore(context);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        WorkerProcess process = CreateProcess(now.AddMinutes(-1), 3);
        await store.RegisterProcessAsync(process, CancellationToken.None);
        context.ChangeTracker.Clear();
        await store.HeartbeatProcessAsync(process.Id, now, CancellationToken.None);
        await store.HeartbeatProcessAsync(process.Id, now.AddSeconds(-10), CancellationToken.None);
        WorkerProcess persisted = await context.WorkerProcesses.AsNoTracking().SingleAsync();
        persisted.LastHeartbeatAtUtc.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
        persisted.WorkerCount.Should().Be(3);
        context.JobExecutions.Should().BeEmpty();
        await store.StopProcessAsync(process.Id, now.AddSeconds(1), CancellationToken.None);
        await store.StopProcessAsync(process.Id, now.AddSeconds(2), CancellationToken.None);
        await store.HeartbeatProcessAsync(process.Id, now.AddMinutes(1), CancellationToken.None);
        persisted = await context.WorkerProcesses.AsNoTracking().SingleAsync();
        persisted.StoppedAtUtc.Should().BeCloseTo(now.AddSeconds(1), TimeSpan.FromMilliseconds(1));
        persisted.LastHeartbeatAtUtc.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));

        WorkerProcess invalid = CreateProcess(now, 1);
        invalid.Id = "legacy-host";
        Func<Task> registerInvalid = () => store.RegisterProcessAsync(invalid, CancellationToken.None);
        await registerInvalid.Should().ThrowAsync<ArgumentException>();
        await store.DeleteExecutionHistoryAsync(now.AddMinutes(2), CancellationToken.None);
        (await context.WorkerProcesses.CountAsync()).Should().Be(1);
        await context.WorkerProcesses.ExecuteUpdateAsync(update => update.SetProperty(item => item.LastHeartbeatAtUtc, now.AddMinutes(-1)));
        await store.DeleteExecutionHistoryAsync(now.AddMinutes(2), CancellationToken.None);
        (await context.WorkerProcesses.CountAsync()).Should().Be(0);
    }

    /// <summary>Verifies exact process ownership, stale states, short-job transitions, and safe read-only projection.</summary>
    [Fact]
    public async Task FactoryProjectsRealProcessesAndPersistedTransitionsAsync()
    {
        await using KidevDbContext context = CreateDbContext();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        WorkerProcess online = CreateProcess(now, 2);
        WorkerProcess stale = CreateProcess(now.AddMinutes(-1), 1);
        WorkerProcess stopped = CreateProcess(now, 1);
        stopped.StoppedAtUtc = now;
        context.WorkerProcesses.AddRange(online, stale, stopped);
        JobDefinition job = CreateJobDefinition("factory-job", "* * * * *", "secret-argument");
        context.JobDefinitions.Add(job);
        await context.SaveChangesAsync();
        JobExecution succeeded = CreateAttempt(job.Id, online.Id + ":0", now.AddSeconds(-2));
        succeeded.CompletedAtUtc = now.AddSeconds(-1);
        succeeded.Status = JobExecutionStatus.Succeeded;
        JobExecution expired = CreateAttempt(job.Id, stale.Id + ":0", now.AddMinutes(-5));
        expired.LeaseExpiresAtUtc = now.AddSeconds(-5);
        JobExecution failed = CreateAttempt(job.Id, "legacy-worker", now.AddSeconds(-3));
        failed.Status = JobExecutionStatus.Failed;
        failed.CompletedAtUtc = now.AddSeconds(-1);
        failed.ErrorMessage = "Password=do-not-expose; secret-argument";
        failed.ErrorType = "SensitiveCustomerException";
        JobExecution invalidSlot = CreateAttempt(job.Id, online.Id + ":02", now.AddSeconds(-2));
        JobExecution longRunning = CreateAttempt(job.Id, online.Id + ":1", now.AddHours(-1));
        longRunning.LeaseExpiresAtUtc = now.AddMinutes(1);
        context.JobExecutions.AddRange(succeeded, expired, failed, invalidSlot, longRunning);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var query = new PostgreSqlDashboardQuery(context);
        FactorySnapshot snapshot = await query.ReadFactoryAsync(CancellationToken.None);
        snapshot.IsTruncated.Should().BeFalse();
        snapshot.Processes.Single(process => string.Equals(process.Id, online.Id, StringComparison.Ordinal)).State.Should().Be("Online");
        snapshot.Processes.Single(process => string.Equals(process.Id, stale.Id, StringComparison.Ordinal)).State.Should().Be("Stale");
        snapshot.Processes.Single(process => string.Equals(process.Id, stopped.Id, StringComparison.Ordinal)).State.Should().Be("Stopped");
        snapshot.Executions.Single(attempt => attempt.Id == succeeded.Id).ProcessInstanceId.Should().Be(online.Id);
        snapshot.Executions.Single(attempt => attempt.Id == invalidSlot.Id).ProcessInstanceId.Should().BeNull();
        snapshot.Executions.Single(attempt => attempt.Id == failed.Id).ProcessInstanceId.Should().BeNull();
        snapshot.Executions.Single(attempt => attempt.Id == expired.Id).Status.Should().Be("LeaseExpired");
        snapshot.Executions.Single(attempt => attempt.Id == expired.Id).CompletedAtUtc.Should().BeNull();
        snapshot.Executions.Single(attempt => attempt.Id == longRunning.Id).Status.Should().Be("Running");
        snapshot.Events.Where(item => item.ClaimId == succeeded.ClaimId).Select(item => item.State)
            .Should().BeEquivalentTo("Started", "Succeeded");
        snapshot.Events.Single(item => item.ClaimId == expired.ClaimId).IsInferred.Should().BeTrue();
        snapshot.Events.Should().NotContain(item => item.ClaimId == longRunning.ClaimId);
        snapshot.Events.Should().OnlyContain(item => item.OccurredAtUtc >= snapshot.WindowStartAtUtc && item.OccurredAtUtc <= snapshot.ObservedAtUtc);
        string json = JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web);
        json.Should().NotContain("argumentsJson").And.NotContain("secret-argument")
            .And.NotContain("Password=").And.NotContain("SensitiveCustomerException");
        context.ChangeTracker.Entries().Should().BeEmpty();
        (await context.JobExecutions.AsNoTracking().SingleAsync(attempt => attempt.Id == expired.Id))
            .Status.Should().Be(JobExecutionStatus.Running);

        FactorySnapshot repeated = await query.ReadFactoryAsync(CancellationToken.None);
        repeated.Events.Select(item => item.EventId).Should().BeEquivalentTo(snapshot.Events.Select(item => item.EventId));
        var store = new PostgreSqlJobDefinitionStore(context);
        await store.ExpireLeasesAsync(now, CancellationToken.None);
        FactorySnapshot finalized = await query.ReadFactoryAsync(CancellationToken.None);
        FactoryEvent recorded = finalized.Events.Single(item => item.ClaimId == expired.ClaimId);
        recorded.IsInferred.Should().BeFalse();
        recorded.EventId.Should().Be(snapshot.Events.Single(item => item.ClaimId == expired.ClaimId).EventId);
    }

    /// <summary>Verifies all DTO limits and that short completed attempts remain visible beside many live jobs.</summary>
    [Fact]
    public async Task FactoryEnforcesGlobalBudgetsWithoutLosingShortJobHistoryAsync()
    {
        await using KidevDbContext context = CreateDbContext();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 25; index++)
        {
            context.WorkerProcesses.Add(CreateProcess(now, 10));
        }

        for (int index = 0; index < 105; index++)
        {
            context.JobDefinitions.Add(CreateJobDefinition("factory-" + index.ToString(CultureInfo.InvariantCulture), "* * * * *"));
        }

        await context.SaveChangesAsync();
        int id = await context.JobDefinitions.Select(job => job.Id).FirstAsync();
        for (int index = 0; index < 105; index++)
        {
            JobExecution running = CreateAttempt(id, "legacy", now.AddHours(-1));
            running.LeaseExpiresAtUtc = now.AddMinutes(5);
            context.JobExecutions.Add(running);
            JobExecution shortJob = CreateAttempt(id, "legacy", now.AddSeconds(-10));
            shortJob.Status = JobExecutionStatus.Succeeded;
            shortJob.CompletedAtUtc = now.AddSeconds(-5);
            context.JobExecutions.Add(shortJob);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        FactorySnapshot snapshot = await new PostgreSqlDashboardQuery(context).ReadFactoryAsync(CancellationToken.None);
        snapshot.IsTruncated.Should().BeTrue();
        snapshot.Processes.Should().HaveCount(20);
        snapshot.Processes.Sum(process => process.WorkerCount).Should().Be(128);
        snapshot.Processes.Should().OnlyContain(process => process.ConfiguredWorkerCount == 10);
        snapshot.Jobs.Should().HaveCount(100);
        snapshot.Executions.Should().HaveCount(100).And.OnlyContain(attempt => attempt.Status == "Running");
        snapshot.Events.Should().HaveCount(100).And.OnlyContain(item => item.State == "Succeeded");
        snapshot.Events.Select(item => item.EventId).Should().OnlyHaveUniqueItems();
    }

    private static WorkerProcess CreateProcess(DateTimeOffset now, int workerCount) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = "Factory test host",
        MachineName = "test-machine",
        ProcessId = 1234,
        WorkerCount = workerCount,
        StartedAtUtc = now,
        LastHeartbeatAtUtc = now,
    };

    private static JobExecution CreateAttempt(int jobId, string workerId, DateTimeOffset startedAtUtc) => new()
    {
        JobDefinitionId = jobId,
        ClaimId = Guid.NewGuid(),
        WorkerId = workerId,
        StartedAtUtc = startedAtUtc,
        LastHeartbeatAtUtc = startedAtUtc,
        LeaseExpiresAtUtc = startedAtUtc.AddMinutes(5),
        Status = JobExecutionStatus.Running,
    };
}
