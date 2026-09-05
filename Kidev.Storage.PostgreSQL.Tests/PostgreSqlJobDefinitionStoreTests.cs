using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kidev.Storage.PostgreSQL.Tests;

/// <summary>
/// Verifies PostgreSQL job definition persistence against a real database container.
/// </summary>
public sealed class PostgreSqlJobDefinitionStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await database.StartAsync();

        await using KidevDbContext dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return database.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Verifies synchronization creates persisted jobs with their first scheduled execution time.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsyncCreatesRegisteredJobs()
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        JobDefinition[] registeredJobDefinitions =
        {
            CreateJobDefinition("daily-report", "0 * * * *"),
            CreateJobDefinition("hourly-cleanup", "0 */2 * * *")
        };

        await using (KidevDbContext dbContext = CreateDbContext())
        {
            var store = new PostgreSqlJobDefinitionStore(dbContext);
            await store.SynchronizeAsync(registeredJobDefinitions, CancellationToken.None);
        }

        await using KidevDbContext assertionContext = CreateDbContext();
        List<JobDefinition> persistedJobDefinitions = await assertionContext.JobDefinitions
            .OrderBy(job => job.RegistrationKey)
            .ToListAsync(CancellationToken.None);

        persistedJobDefinitions.Should().HaveCount(2);
        persistedJobDefinitions.Select(job => job.RegistrationKey)
            .Should().ContainInOrder("daily-report", "hourly-cleanup");
        persistedJobDefinitions.Should().OnlyContain(job => job.IsEnabled);
        persistedJobDefinitions.Should().OnlyContain(job => job.LastExecutedAtUtc == null);
        persistedJobDefinitions.Should().OnlyContain(job => job.NextExecutionAtUtc > startedAtUtc);
    }

    /// <summary>
    /// Verifies synchronization updates registered jobs, recalculates changed schedules, and disables removed jobs.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsyncReconcilesChangedAndRemovedJobs()
    {
        JobDefinition originalJobDefinition = CreateJobDefinition("daily-report", "0 * * * *");
        JobDefinition removedJobDefinition = CreateJobDefinition("retired-job", "0 * * * *");

        await using (KidevDbContext dbContext = CreateDbContext())
        {
            var store = new PostgreSqlJobDefinitionStore(dbContext);
            await store.SynchronizeAsync([originalJobDefinition, removedJobDefinition], CancellationToken.None);
        }

        DateTimeOffset lastExecutedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset previousNextExecutionAtUtc = DateTimeOffset.UtcNow.AddHours(1);

        await using (KidevDbContext dbContext = CreateDbContext())
        {
            JobDefinition persistedJobDefinition = await dbContext.JobDefinitions
                .SingleAsync(job => job.RegistrationKey == originalJobDefinition.RegistrationKey, CancellationToken.None);
            persistedJobDefinition.LastExecutedAtUtc = lastExecutedAtUtc;
            persistedJobDefinition.NextExecutionAtUtc = previousNextExecutionAtUtc;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        DateTimeOffset synchronizedAtUtc = DateTimeOffset.UtcNow;
        JobDefinition changedJobDefinition = CreateJobDefinition("daily-report", "*/5 * * * *", "updated-arguments");

        await using (KidevDbContext dbContext = CreateDbContext())
        {
            var store = new PostgreSqlJobDefinitionStore(dbContext);
            await store.SynchronizeAsync([changedJobDefinition], CancellationToken.None);
        }

        await using KidevDbContext assertionContext = CreateDbContext();
        JobDefinition updatedJobDefinition = await assertionContext.JobDefinitions
            .SingleAsync(job => job.RegistrationKey == changedJobDefinition.RegistrationKey, CancellationToken.None);
        JobDefinition disabledJobDefinition = await assertionContext.JobDefinitions
            .SingleAsync(job => job.RegistrationKey == removedJobDefinition.RegistrationKey, CancellationToken.None);

        updatedJobDefinition.ArgumentsJson.Should().Be("[\"updated-arguments\"]");
        updatedJobDefinition.CronExpression.Should().Be("*/5 * * * *");
        updatedJobDefinition.IsEnabled.Should().BeTrue();
        updatedJobDefinition.LastExecutedAtUtc.Should().BeCloseTo(lastExecutedAtUtc, TimeSpan.FromMicroseconds(1));
        updatedJobDefinition.NextExecutionAtUtc.Should().BeAfter(synchronizedAtUtc);
        updatedJobDefinition.NextExecutionAtUtc.Should().NotBe(previousNextExecutionAtUtc);
        disabledJobDefinition.IsEnabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies only one worker can claim a due job and that its owner can renew and complete the claim.
    /// </summary>
    [Fact]
    public async Task ClaimNextDueAsyncClaimsJobForOneWorker()
    {
        JobDefinition registeredJobDefinition = CreateJobDefinition("daily-report", "0 * * * *");

        await using (KidevDbContext synchronizationContext = CreateDbContext())
        {
            var synchronizationStore = new PostgreSqlJobDefinitionStore(synchronizationContext);
            await synchronizationStore.SynchronizeAsync([registeredJobDefinition], CancellationToken.None);
        }

        await using (KidevDbContext dueJobContext = CreateDbContext())
        {
            JobDefinition dueJobDefinition = await dueJobContext.JobDefinitions.SingleAsync(CancellationToken.None);
            dueJobDefinition.NextExecutionAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dueJobContext.SaveChangesAsync(CancellationToken.None);
        }

        ClaimedJob? firstClaim;

        await using (KidevDbContext firstWorkerContext = CreateDbContext())
        {
            var firstWorkerStore = new PostgreSqlJobDefinitionStore(firstWorkerContext);
            firstClaim = await firstWorkerStore.ClaimNextDueAsync(
                "instance-a:0",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
        }

        firstClaim.Should().NotBeNull();
        ClaimedJob claimedJob = firstClaim;

        await using (KidevDbContext secondWorkerContext = CreateDbContext())
        {
            var secondWorkerStore = new PostgreSqlJobDefinitionStore(secondWorkerContext);
            ClaimedJob? secondClaim = await secondWorkerStore.ClaimNextDueAsync(
                "instance-a:1",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            secondClaim.Should().BeNull();
        }

        DateTimeOffset renewedLeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5);

        await using (KidevDbContext heartbeatContext = CreateDbContext())
        {
            var heartbeatStore = new PostgreSqlJobDefinitionStore(heartbeatContext);
            bool renewed = await heartbeatStore.RenewLeaseAsync(
                claimedJob.JobDefinition.Id,
                claimedJob.ClaimId,
                renewedLeaseExpiresAtUtc,
                CancellationToken.None);
            renewed.Should().BeTrue();
        }

        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;

        await using (KidevDbContext completionContext = CreateDbContext())
        {
            var completionStore = new PostgreSqlJobDefinitionStore(completionContext);
            await completionStore.CompleteAsync(
                claimedJob.JobDefinition.Id,
                claimedJob.ClaimId,
                completedAtUtc,
                completedAtUtc.AddHours(1),
                CancellationToken.None);
        }

        await using KidevDbContext assertionContext = CreateDbContext();
        JobDefinition completedJobDefinition = await assertionContext.JobDefinitions.SingleAsync(CancellationToken.None);
        completedJobDefinition.ClaimId.Should().BeNull();
        completedJobDefinition.ClaimedBy.Should().BeNull();
        completedJobDefinition.ClaimedAtUtc.Should().BeNull();
        completedJobDefinition.LeaseExpiresAtUtc.Should().BeNull();
        completedJobDefinition.LastExecutedAtUtc.Should().BeCloseTo(completedAtUtc, TimeSpan.FromMicroseconds(1));
        JobExecution execution = await assertionContext.JobExecutions.SingleAsync(CancellationToken.None);
        execution.ClaimId.Should().Be(claimedJob.ClaimId);
        execution.WorkerId.Should().Be("instance-a:0");
        execution.LastHeartbeatAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        execution.LeaseExpiresAtUtc.Should().BeCloseTo(renewedLeaseExpiresAtUtc, TimeSpan.FromMicroseconds(1));
        execution.CompletedAtUtc.Should().BeCloseTo(completedAtUtc, TimeSpan.FromMicroseconds(1));
        execution.Status.Should().Be(JobExecutionStatus.Succeeded);
    }

    /// <summary>
    /// Verifies failed claims clear the lease, preserve failure details, and advance the schedule.
    /// </summary>
    [Fact]
    public async Task FailAsyncCompletesExecutionAsFailed()
    {
        ClaimedJob claimedJob = await ClaimDueJobAsync();
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        DateTimeOffset nextExecutionAtUtc = completedAtUtc.AddHours(1);

        await using (KidevDbContext failureContext = CreateDbContext())
        {
            var failureStore = new PostgreSqlJobDefinitionStore(failureContext);
            await failureStore.FailAsync(
                claimedJob.JobDefinition.Id,
                claimedJob.ClaimId,
                completedAtUtc,
                nextExecutionAtUtc,
                "System.InvalidOperationException",
                "Job failed.",
                CancellationToken.None);
        }

        await using KidevDbContext assertionContext = CreateDbContext();
        JobDefinition jobDefinition = await assertionContext.JobDefinitions.SingleAsync(CancellationToken.None);
        JobExecution execution = await assertionContext.JobExecutions.SingleAsync(CancellationToken.None);
        jobDefinition.ClaimId.Should().BeNull();
        jobDefinition.NextExecutionAtUtc.Should().BeCloseTo(nextExecutionAtUtc, TimeSpan.FromMicroseconds(1));
        execution.Status.Should().Be(JobExecutionStatus.Failed);
        execution.CompletedAtUtc.Should().BeCloseTo(completedAtUtc, TimeSpan.FromMicroseconds(1));
        execution.ErrorType.Should().Be("System.InvalidOperationException");
        execution.ErrorMessage.Should().Be("Job failed.");
    }

    /// <summary>
    /// Verifies expired running executions are finalized and completed history can be removed.
    /// </summary>
    [Fact]
    public async Task ExpireLeasesAndDeleteExecutionHistoryAsyncFinalizeAndRemoveHistory()
    {
        ClaimedJob claimedJob = await ClaimDueJobAsync();
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;

        await using (KidevDbContext expiryContext = CreateDbContext())
        {
            JobExecution runningExecution = await expiryContext.JobExecutions.SingleAsync(CancellationToken.None);
            runningExecution.LeaseExpiresAtUtc = utcNow.AddMinutes(-1);
            await expiryContext.SaveChangesAsync(CancellationToken.None);

            var expiryStore = new PostgreSqlJobDefinitionStore(expiryContext);
            await expiryStore.ExpireLeasesAsync(utcNow, CancellationToken.None);
        }

        await using (KidevDbContext assertionContext = CreateDbContext())
        {
            JobExecution execution = await assertionContext.JobExecutions.SingleAsync(CancellationToken.None);
            execution.ClaimId.Should().Be(claimedJob.ClaimId);
            execution.Status.Should().Be(JobExecutionStatus.LeaseExpired);
            execution.CompletedAtUtc.Should().BeCloseTo(utcNow, TimeSpan.FromMicroseconds(1));
            execution.Reason.Should().Be("Lease expired.");
        }

        await using (KidevDbContext deletionContext = CreateDbContext())
        {
            var deletionStore = new PostgreSqlJobDefinitionStore(deletionContext);
            await deletionStore.DeleteExecutionHistoryAsync(utcNow.AddMinutes(1), CancellationToken.None);
        }

        await using KidevDbContext finalContext = CreateDbContext();
        (await finalContext.JobExecutions.CountAsync(CancellationToken.None)).Should().Be(0);
    }

    private async Task<ClaimedJob> ClaimDueJobAsync()
    {
        JobDefinition registeredJobDefinition = CreateJobDefinition("daily-report", "0 * * * *");

        await using (KidevDbContext synchronizationContext = CreateDbContext())
        {
            var synchronizationStore = new PostgreSqlJobDefinitionStore(synchronizationContext);
            await synchronizationStore.SynchronizeAsync([registeredJobDefinition], CancellationToken.None);
        }

        await using (KidevDbContext dueJobContext = CreateDbContext())
        {
            JobDefinition dueJobDefinition = await dueJobContext.JobDefinitions.SingleAsync(CancellationToken.None);
            dueJobDefinition.NextExecutionAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dueJobContext.SaveChangesAsync(CancellationToken.None);
        }

        await using KidevDbContext claimContext = CreateDbContext();
        var claimStore = new PostgreSqlJobDefinitionStore(claimContext);
        ClaimedJob? claimedJob = await claimStore.ClaimNextDueAsync(
            "instance-a:0",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        claimedJob.Should().NotBeNull();
        return claimedJob;
    }

    private KidevDbContext CreateDbContext()
    {
        DbContextOptions<KidevDbContext> options = new DbContextOptionsBuilder<KidevDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;

        return new KidevDbContext(options);
    }

    private static JobDefinition CreateJobDefinition(string registrationKey, string cronExpression, string argument = "arguments")
    {
        return new JobDefinition
        {
            RegistrationKey = registrationKey,
            AssemblyName = "Kidev.Tests",
            ServiceTypeName = "Kidev.Tests.JobService, Kidev.Tests",
            MethodName = "Execute",
            MethodParameterTypesJson = "[\"System.String\"]",
            ArgumentsJson = $"[\"{argument}\"]",
            CronExpression = cronExpression,
            TimeZoneId = "UTC"
        };
    }
}
