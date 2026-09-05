using System;
using System.Collections.Generic;
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
    /// Synchronizes the job definitions registered during application startup with persistent storage.
    /// </summary>
    /// <param name="jobDefinitions">The immutable set of registered job definitions.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the synchronization operation.</returns>
    Task SynchronizeAsync(IReadOnlyList<JobDefinition> jobDefinitions, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims the next enabled job whose scheduled execution time has arrived.
    /// </summary>
    /// <param name="workerId">The identifier of the worker claiming the job.</param>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="leaseDuration">The duration for which the claim remains valid without a heartbeat.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The claimed job, or <see langword="null"/> when no job is due.</returns>
    Task<ClaimedJob?> ClaimNextDueAsync(
        string workerId,
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extends an active job claim owned by a worker.
    /// </summary>
    /// <param name="jobId">The database identifier of the claimed job.</param>
    /// <param name="claimId">The identifier of the execution attempt extending the lease.</param>
    /// <param name="leaseExpiresAtUtc">The UTC time at which the renewed claim should expire.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns><see langword="true"/> when the claim was renewed; otherwise, <see langword="false"/>.</returns>
    Task<bool> RenewLeaseAsync(
        int jobId,
        Guid claimId,
        DateTimeOffset leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful execution and advances the job to its next scheduled occurrence.
    /// </summary>
    /// <param name="jobId">The database identifier of the completed job.</param>
    /// <param name="claimId">The identifier of the execution attempt completing the job.</param>
    /// <param name="lastExecutedAtUtc">The UTC time at which the job completed.</param>
    /// <param name="nextExecutionAtUtc">The UTC time at which the job is next due.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that represents the completion update.</returns>
    Task CompleteAsync(
        int jobId,
        Guid claimId,
        DateTimeOffset lastExecutedAtUtc,
        DateTimeOffset nextExecutionAtUtc,
        CancellationToken cancellationToken);
}
