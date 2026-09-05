using Kidev.Core.Data;

namespace Kidev.Dashboard.Models;

/// <summary>Contains execution attempts and the time at which their leases are observed.</summary>
public sealed record ExecutionHistoryViewModel
{
    /// <summary>Gets the execution attempts to display.</summary>
    public required IReadOnlyList<JobExecution> Executions { get; init; }
    /// <summary>Gets the observation timestamp used to display lease status.</summary>
    public DateTimeOffset ObservedAtUtc { get; init; }
}
