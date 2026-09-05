using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kidev.Storage.PostgreSQL.Tests;

/// <summary>Verifies dashboard SQL filters, bounded paging, counts, and no-tracking reads.</summary>
public sealed class PostgreSqlDashboardQueryTests
{
    /// <summary>Verifies query behavior against PostgreSQL rather than an in-memory LINQ provider.</summary>
    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100", Justification = "Database identifier consists solely of a fixed prefix and a generated hexadecimal GUID.")]
    public async Task QueriesAreBoundedFilteredAndReadOnlyAsync()
    {
        string? connectionString = Environment.GetEnvironmentVariable("KIDEV_DASHBOARD_TEST_CONNECTION_STRING");
        await using PostgreSqlContainer? container = connectionString is null ? new PostgreSqlBuilder("postgres:17-alpine").Build() : null;
        if (container is not null)
        {
            await container.StartAsync();
            connectionString = container.GetConnectionString();
        }

        var options = new NpgsqlConnectionStringBuilder(connectionString);
        string databaseName = "dashboard_query_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        options.Database = databaseName;
        await using var context = new KidevDbContext(new DbContextOptionsBuilder<KidevDbContext>().UseNpgsql(options.ConnectionString).Options);
        await context.Database.MigrateAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 61; index++)
        {
            context.JobDefinitions.Add(new JobDefinition
            {
                RegistrationKey = "job-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CronExpression = "* * * * *",
                NextExecutionAtUtc = now.AddHours(-1),
                IsEnabled = index != 60,
            });
        }

        await context.SaveChangesAsync();
        int id = await context.JobDefinitions.OrderBy(job => job.Id).Select(job => job.Id).FirstAsync();
        for (int index = 0; index < 61; index++)
        {
            context.JobExecutions.Add(new JobExecution
            {
                JobDefinitionId = id,
                ClaimId = Guid.NewGuid(),
                WorkerId = "worker-test",
                StartedAtUtc = now.AddMinutes(-index),
                LastHeartbeatAtUtc = now,
                LeaseExpiresAtUtc = index == 0 ? now.AddHours(1) : now.AddHours(-1),
                Status = index < 2 ? JobExecutionStatus.Running : JobExecutionStatus.Failed,
            });
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var query = new PostgreSqlDashboardQuery(context);
        DashboardSnapshot first = await query.ReadAsync(null, null, 1, CancellationToken.None);
        first.TotalJobs.Should().Be(61);
        first.DueJobs.Should().Be(60);
        first.Running.Should().Be(1);
        first.Expired.Should().Be(1);
        first.Failed.Should().Be(59);
        first.Jobs.Should().HaveCount(50);
        first.Executions.Should().HaveCount(50);
        first.HasNextJobPage.Should().BeTrue();
        first.HasNextExecutionPage.Should().BeTrue();
        DashboardSnapshot second = await query.ReadAsync(null, null, 2, CancellationToken.None);
        second.Jobs.Should().HaveCount(11);
        second.Executions.Should().HaveCount(11);
        second.HasNextJobPage.Should().BeFalse();
        second.HasNextExecutionPage.Should().BeFalse();
        first.Jobs.Select(job => job.Id).Should().NotIntersectWith(second.Jobs.Select(job => job.Id));
        DashboardSnapshot failed = await query.ReadAsync("job-0", "Failed", 1, CancellationToken.None);
        failed.Jobs.Should().ContainSingle().Which.Id.Should().Be(id);
        failed.Executions.Should().HaveCount(50).And.OnlyContain(execution => execution.Status == JobExecutionStatus.Failed);
        failed.HasNextJobPage.Should().BeFalse();
        failed.HasNextExecutionPage.Should().BeTrue();
        DashboardSnapshot expired = await query.ReadAsync(null, "Expired", 1, CancellationToken.None);
        expired.Executions.Should().ContainSingle();
        expired.Executions[0].Status.Should().Be(JobExecutionStatus.Running);
        expired.Executions[0].LeaseExpiresAtUtc.Should().BeOnOrBefore(expired.ObservedAtUtc);
        DashboardSnapshot disabled = await query.ReadAsync(null, "Disabled", 1, CancellationToken.None);
        disabled.Jobs.Should().ContainSingle().Which.IsEnabled.Should().BeFalse();
        DashboardSnapshot empty = await query.ReadAsync("absent", null, 1, CancellationToken.None);
        empty.Jobs.Should().BeEmpty();
        empty.Executions.Should().BeEmpty();
        DashboardJobDetails? details = await query.FindAsync(id, CancellationToken.None);
        details.Should().NotBeNull();
        details.Executions.Should().HaveCount(50);
        (await query.FindAsync(int.MaxValue, CancellationToken.None)).Should().BeNull();
        context.ChangeTracker.Entries().Should().BeEmpty();

        await context.JobDefinitions.Where(job => job.Id != id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.NextExecutionAtUtc, now.AddHours(1)));
        DashboardSnapshot scheduled = await query.ReadAsync(null, "Scheduled", 1, CancellationToken.None);
        scheduled.Jobs.Should().HaveCount(50);
        scheduled.Executions.Should().BeEmpty();
        scheduled.HasNextJobPage.Should().BeTrue();
        scheduled.HasNextExecutionPage.Should().BeFalse();
        DashboardSnapshot scheduledLast = await query.ReadAsync(null, "Scheduled", 2, CancellationToken.None);
        scheduledLast.Jobs.Should().HaveCount(9);
        scheduledLast.HasNextJobPage.Should().BeFalse();
        scheduledLast.HasNextExecutionPage.Should().BeFalse();

        Func<Task> invalid = () => query.ReadAsync(null, null, 0, CancellationToken.None);
        await invalid.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
