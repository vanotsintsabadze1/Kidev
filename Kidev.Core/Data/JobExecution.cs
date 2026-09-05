using System;

namespace Kidev.Core.Data;

/// <summary>
/// Records one attempt to execute a recurring job.
/// </summary>
public sealed class JobExecution
{
    /// <summary>Gets or sets the database-generated execution identifier.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the identifier of the recurring job definition.</summary>
    public int JobDefinitionId { get; set; }

    /// <summary>Gets or sets the identifier of this execution attempt's claim.</summary>
    public Guid ClaimId { get; set; }

    /// <summary>Gets or sets the worker that claimed the job.</summary>
    public string WorkerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC time at which execution started.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>Gets or sets the UTC time of the most recent successful heartbeat.</summary>
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }

    /// <summary>Gets or sets the UTC time at which the current lease expires.</summary>
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the UTC time at which execution reached its final state.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Gets or sets the final status of the execution attempt.</summary>
    public JobExecutionStatus Status { get; set; }

    /// <summary>Gets or sets the reason the execution reached its final state.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets the exception type when execution fails.</summary>
    public string? ErrorType { get; set; }

    /// <summary>Gets or sets the limited exception message when execution fails.</summary>
    public string? ErrorMessage { get; set; }
}
