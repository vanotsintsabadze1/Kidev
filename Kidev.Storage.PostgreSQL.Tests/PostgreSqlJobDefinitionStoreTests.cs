using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
