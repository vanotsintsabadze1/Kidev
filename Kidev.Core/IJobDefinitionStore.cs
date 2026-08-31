using System;
using System.Threading;
using System.Threading.Tasks;
using Kidev.Core.Data;

namespace Kidev.Core;

/// <summary>
/// Retrieves persisted job definitions that are ready to execute.
/// </summary>
public interface IJobDefinitionStore
{
    /// <summary>
    /// Gets the next enabled job whose scheduled execution time has arrived.
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The next due job, or <see langword="null"/> when no job is due.</returns>
    Task<JobDefinition?> GetNextDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful execution and advances the job to its next scheduled occurrence.
    /// </summary>
    /// <param name="jobId">The database identifier of the completed job.</param>
    /// <param name="lastExecutedAtUtc">The UTC time at which the job completed.</param>
    /// <param name="nextExecutionAtUtc">The UTC time at which the job is next due.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the completion update.</returns>
    Task CompleteAsync(
        int jobId,
        DateTimeOffset lastExecutedAtUtc,
        DateTimeOffset nextExecutionAtUtc,
        CancellationToken cancellationToken);
}
