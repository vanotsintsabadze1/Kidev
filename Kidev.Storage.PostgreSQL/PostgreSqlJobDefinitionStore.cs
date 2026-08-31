using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Kidev.Storage.PostgreSQL;

/// <summary>
/// Retrieves due job definitions from PostgreSQL.
/// </summary>
internal sealed class PostgreSqlJobDefinitionStore(KidevDbContext dbContext) : IJobDefinitionStore
{
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
}
