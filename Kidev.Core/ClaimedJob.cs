using System;
using Kidev.Core.Data;

namespace Kidev.Core;

/// <summary>
/// Represents a job definition claimed by a worker for one execution attempt.
/// </summary>
public sealed class ClaimedJob
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimedJob"/> class.
    /// </summary>
    /// <param name="jobDefinition">The claimed job definition.</param>
    /// <param name="claimId">The identifier that proves ownership of this execution attempt.</param>
    public ClaimedJob(JobDefinition jobDefinition, Guid claimId)
    {
        JobDefinition = jobDefinition ?? throw new ArgumentNullException(nameof(jobDefinition));
        ClaimId = claimId;
    }

    /// <summary>
    /// Gets the claimed job definition.
    /// </summary>
    public JobDefinition JobDefinition { get; }

    /// <summary>
    /// Gets the identifier that proves ownership of this execution attempt.
    /// </summary>
    public Guid ClaimId { get; }

}
