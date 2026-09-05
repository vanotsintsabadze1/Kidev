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

            bool scheduleChanged = persistedJobDefinition.CronExpression != registeredJobDefinition.CronExpression
                || persistedJobDefinition.TimeZoneId != registeredJobDefinition.TimeZoneId;

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
    public Task<JobDefinition?> GetNextDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        return dbContext.JobDefinitions
            .AsNoTracking()
            .Where(job => job.IsEnabled && job.NextExecutionAtUtc <= utcNow)
            .OrderBy(job => job.NextExecutionAtUtc)
            .ThenBy(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        int jobId,
        DateTimeOffset lastExecutedAtUtc,
        DateTimeOffset nextExecutionAtUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.JobDefinitions
            .Where(job => job.Id == jobId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(job => job.LastExecutedAtUtc, lastExecutedAtUtc)
                    .SetProperty(job => job.NextExecutionAtUtc, nextExecutionAtUtc),
                cancellationToken);
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
