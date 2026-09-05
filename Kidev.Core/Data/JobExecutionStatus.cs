namespace Kidev.Core.Data;

/// <summary>
/// Describes the lifecycle state of a job execution attempt.
/// </summary>
public enum JobExecutionStatus
{
    /// <summary>The worker owns an active lease and is executing the job.</summary>
    Running,

    /// <summary>The job completed successfully.</summary>
    Succeeded,

    /// <summary>The job threw an exception.</summary>
    Failed,

    /// <summary>The worker did not renew its lease before it expired.</summary>
    LeaseExpired
}
