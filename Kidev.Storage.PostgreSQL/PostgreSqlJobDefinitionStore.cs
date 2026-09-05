using Cronos;
using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kidev.Storage.PostgreSQL;

/// <summary>
/// Retrieves due job definitions from PostgreSQL.
/// </summary>
internal sealed class PostgreSqlJobDefinitionStore(KidevDbContext dbContext) : IJobDefinitionStore
{
    /// <inheritdoc />
    public async Task SynchronizeAsync(IReadOnlyList<JobDefinition> jobDefinitions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobDefinitions);

        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        var registeredJobDefinitions = new Dictionary<string, JobDefinition>(StringComparer.Ordinal);

        foreach (JobDefinition jobDefinition in jobDefinitions)
        {
            registeredJobDefinitions.Add(jobDefinition.RegistrationKey, jobDefinition);
        }

        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        List<JobDefinition> persistedJobDefinitions = await dbContext.JobDefinitions.ToListAsync(cancellationToken);
        var persistedByRegistrationKey = persistedJobDefinitions.ToDictionary(job => job.RegistrationKey, StringComparer.Ordinal);

        foreach ((string registrationKey, JobDefinition registeredJobDefinition) in registeredJobDefinitions)
        {
            if (!persistedByRegistrationKey.TryGetValue(registrationKey, out JobDefinition? persistedJobDefinition))
            {
                dbContext.JobDefinitions.Add(CreateNewJobDefinition(registeredJobDefinition, utcNow));
                continue;
            }

            bool scheduleChanged = !string.Equals(
                    persistedJobDefinition.CronExpression,
                    registeredJobDefinition.CronExpression,
                    StringComparison.Ordinal)
                || !string.Equals(
                    persistedJobDefinition.TimeZoneId,
                    registeredJobDefinition.TimeZoneId,
                    StringComparison.Ordinal);

            CopyRegistrationDefinition(registeredJobDefinition, persistedJobDefinition);
            persistedJobDefinition.IsEnabled = true;

            if (scheduleChanged)
            {
                persistedJobDefinition.NextExecutionAtUtc = GetNextExecutionAtUtc(persistedJobDefinition, utcNow);
            }
        }

        foreach (JobDefinition persistedJobDefinition in persistedJobDefinitions)
        {
            if (!registeredJobDefinitions.ContainsKey(persistedJobDefinition.RegistrationKey))
            {
                persistedJobDefinition.IsEnabled = false;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ClaimedJob?> ClaimNextDueAsync(
        string workerId,
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }

        var claimId = Guid.NewGuid();
        DateTimeOffset leaseExpiresAtUtc = utcNow.Add(leaseDuration);
        List<JobDefinition> claimedJobDefinitions = await dbContext.JobDefinitions
            .FromSqlInterpolated($"""
                WITH candidate AS (
                    SELECT id
                    FROM job_definitions
                    WHERE is_enabled
                        AND next_execution_at_utc <= {utcNow}
                        AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc <= {utcNow})
                    ORDER BY next_execution_at_utc, id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                UPDATE job_definitions AS jobs
                SET claim_id = {claimId},
                    claimed_by = {workerId},
                    claimed_at_utc = {utcNow},
                    lease_expires_at_utc = {leaseExpiresAtUtc}
                FROM candidate
                WHERE jobs.id = candidate.id
                RETURNING jobs.*
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        JobDefinition? claimedJobDefinition = claimedJobDefinitions.SingleOrDefault();
        return claimedJobDefinition is null ? null : new ClaimedJob(claimedJobDefinition, claimId);
    }

    /// <inheritdoc />
    public async Task<bool> RenewLeaseAsync(
        int jobId,
        Guid claimId,
        DateTimeOffset leaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        int affectedRows = await dbContext.JobDefinitions
            .Where(job => job.Id == jobId && job.ClaimId == claimId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(job => job.LeaseExpiresAtUtc, leaseExpiresAtUtc),
                cancellationToken);

        return affectedRows == 1;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        int jobId,
        Guid claimId,
        DateTimeOffset lastExecutedAtUtc,
        DateTimeOffset nextExecutionAtUtc,
        CancellationToken cancellationToken)
    {
        int affectedRows = await dbContext.JobDefinitions
            .Where(job => job.Id == jobId && job.ClaimId == claimId && job.LeaseExpiresAtUtc > lastExecutedAtUtc)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(job => job.LastExecutedAtUtc, lastExecutedAtUtc)
                    .SetProperty(job => job.NextExecutionAtUtc, nextExecutionAtUtc)
                    .SetProperty(job => job.ClaimId, (Guid?)null)
                    .SetProperty(job => job.ClaimedBy, (string?)null)
                    .SetProperty(job => job.ClaimedAtUtc, (DateTimeOffset?)null)
                    .SetProperty(job => job.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);

        if (affectedRows == 0)
        {
            throw new InvalidOperationException($"The claim '{claimId}' no longer owns job '{jobId}'.");
        }
    }

    private static JobDefinition CreateNewJobDefinition(JobDefinition registeredJobDefinition, DateTimeOffset utcNow)
    {
        var jobDefinition = new JobDefinition();
        CopyRegistrationDefinition(registeredJobDefinition, jobDefinition);
        jobDefinition.NextExecutionAtUtc = GetNextExecutionAtUtc(jobDefinition, utcNow);
        jobDefinition.IsEnabled = true;
        return jobDefinition;
    }

    private static void CopyRegistrationDefinition(JobDefinition source, JobDefinition destination)
    {
        destination.RegistrationKey = source.RegistrationKey;
        destination.AssemblyName = source.AssemblyName;
        destination.ServiceTypeName = source.ServiceTypeName;
        destination.MethodName = source.MethodName;
        destination.MethodParameterTypesJson = source.MethodParameterTypesJson;
        destination.ArgumentsJson = source.ArgumentsJson;
        destination.CronExpression = source.CronExpression;
        destination.TimeZoneId = source.TimeZoneId;
    }

    private static DateTimeOffset GetNextExecutionAtUtc(JobDefinition jobDefinition, DateTimeOffset utcNow)
    {
        var cronExpression = CronExpression.Parse(jobDefinition.CronExpression);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(jobDefinition.TimeZoneId);
        DateTime nextExecutionAtUtc = cronExpression.GetNextOccurrence(utcNow.UtcDateTime, timeZone)
            ?? throw new InvalidOperationException($"The schedule for job '{jobDefinition.RegistrationKey}' has no future occurrence.");

        return new DateTimeOffset(nextExecutionAtUtc);
    }
}
