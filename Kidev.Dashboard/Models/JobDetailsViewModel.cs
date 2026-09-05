using Kidev.Core.Data;

namespace Kidev.Dashboard.Models;

/// <summary>Contains a definition and its latest fifty execution attempts.</summary>
public sealed record JobDetailsViewModel
{
    /// <summary>Gets the selected job.</summary>
    public required JobDefinition Job { get; init; }
    /// <summary>Gets the recent execution attempts.</summary>
    public IReadOnlyList<JobExecution> Executions { get; init; } = [];
    /// <summary>Gets the observation timestamp used to display lease status.</summary>
    public DateTimeOffset ObservedAtUtc { get; init; }
}
