using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Kidev.Storage.PostgreSQL;

internal sealed partial class PostgreSqlDashboardQuery(KidevDbContext context) : IDashboardQuery
{
    public async Task<DashboardSnapshot> ReadAsync(string? search, string? status, int page, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, 10000);
        if (search?.Length > 200)
        {
            throw new ArgumentException("Search is limited to 200 characters.", nameof(search));
        }

        if (status is not (null or "" or "Running" or "Succeeded" or "Failed" or "Expired" or "Scheduled" or "Due" or "Disabled"))
        {
            throw new ArgumentException("Invalid dashboard status.", nameof(status));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        IQueryable<JobDefinition> jobs = context.JobDefinitions.AsNoTracking();
        IQueryable<JobExecution> executions = context.JobExecutions.AsNoTracking();
        long total = await jobs.LongCountAsync(cancellationToken);
        long due = await jobs.LongCountAsync(j => j.IsEnabled && j.NextExecutionAtUtc <= now &&
            (j.LeaseExpiresAtUtc == null || j.LeaseExpiresAtUtc <= now), cancellationToken);
        long running = await executions.LongCountAsync(e => e.Status == JobExecutionStatus.Running && e.LeaseExpiresAtUtc > now, cancellationToken);
        long succeeded = await executions.LongCountAsync(e => e.Status == JobExecutionStatus.Succeeded, cancellationToken);
        long failed = await executions.LongCountAsync(e => e.Status == JobExecutionStatus.Failed, cancellationToken);
        long expired = await executions.LongCountAsync(e => e.Status == JobExecutionStatus.LeaseExpired ||
            (e.Status == JobExecutionStatus.Running && e.LeaseExpiresAtUtc <= now), cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            jobs = jobs.Where(j => j.RegistrationKey.Contains(search) || j.ServiceTypeName.Contains(search) || j.MethodName.Contains(search));
        }

        executions = status switch
        {
            "Running" => executions.Where(e => e.Status == JobExecutionStatus.Running && e.LeaseExpiresAtUtc > now),
            "Succeeded" => executions.Where(e => e.Status == JobExecutionStatus.Succeeded),
            "Failed" => executions.Where(e => e.Status == JobExecutionStatus.Failed),
            "Expired" => executions.Where(e => e.Status == JobExecutionStatus.LeaseExpired ||
                (e.Status == JobExecutionStatus.Running && e.LeaseExpiresAtUtc <= now)),
            _ => executions,
        };
        if (string.Equals(status, "Scheduled", StringComparison.Ordinal))
        {
            jobs = jobs.Where(j => j.IsEnabled && j.NextExecutionAtUtc > now);
        }
        else if (string.Equals(status, "Due", StringComparison.Ordinal))
        {
            jobs = jobs.Where(j => j.IsEnabled && j.NextExecutionAtUtc <= now &&
                (j.LeaseExpiresAtUtc == null || j.LeaseExpiresAtUtc <= now));
        }
        else if (string.Equals(status, "Disabled", StringComparison.Ordinal))
        {
            jobs = jobs.Where(j => !j.IsEnabled);
        }
        else if (!string.IsNullOrEmpty(status))
        {
            IQueryable<int> matchingExecutionJobIds = executions.Select(e => e.JobDefinitionId);
            jobs = jobs.Where(j => matchingExecutionJobIds.Contains(j.Id));
        }

        IQueryable<int> matchingJobIds = jobs.Select(j => j.Id);
        executions = executions.Where(e => matchingJobIds.Contains(e.JobDefinitionId));
        const int pageSize = 50;
        int offset = (page - 1) * pageSize;
        List<JobDefinition> jobPage = await jobs.OrderBy(j => j.Id).Skip(offset).Take(pageSize + 1).ToListAsync(cancellationToken);
        List<JobExecution> executionPage = await executions.OrderByDescending(e => e.StartedAtUtc).ThenByDescending(e => e.Id)
            .Skip(offset).Take(pageSize + 1).ToListAsync(cancellationToken);
        return new DashboardSnapshot(total, due, running, succeeded, failed, expired,
            jobPage.Take(pageSize).ToArray(), executionPage.Take(pageSize).ToArray(), now,
            jobPage.Count > pageSize, executionPage.Count > pageSize);
    }

    public async Task<DashboardJobDetails?> FindAsync(int id, CancellationToken cancellationToken)
    {
        JobDefinition? job = await context.JobDefinitions.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        List<JobExecution> executions = await context.JobExecutions.AsNoTracking().Where(e => e.JobDefinitionId == id)
            .OrderByDescending(e => e.StartedAtUtc).ThenByDescending(e => e.Id).Take(50).ToListAsync(cancellationToken);
        return new DashboardJobDetails(job, executions);
    }
}
