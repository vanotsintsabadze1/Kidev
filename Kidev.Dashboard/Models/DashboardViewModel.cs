using Kidev.Core.Data;

namespace Kidev.Dashboard.Models;

/// <summary>Contains persisted dashboard counts and bounded result pages.</summary>
public sealed record DashboardViewModel
{
    /// <summary>Gets the total definition count.</summary>
    public long TotalJobs { get; init; }
    /// <summary>Gets the claimable due definition count.</summary>
    public long DueJobs { get; init; }
    /// <summary>Gets the live running attempt count.</summary>
    public long Running { get; init; }
    /// <summary>Gets the successful attempt count.</summary>
    public long Succeeded { get; init; }
    /// <summary>Gets the failed attempt count.</summary>
    public long Failed { get; init; }
    /// <summary>Gets the expired attempt count.</summary>
    public long Expired { get; init; }
    /// <summary>Gets the current page of definitions.</summary>
    public IReadOnlyList<JobDefinition> Jobs { get; init; } = [];
    /// <summary>Gets the current page of execution attempts.</summary>
    public IReadOnlyList<JobExecution> Executions { get; init; } = [];
    /// <summary>Gets the observation timestamp.</summary>
    public DateTimeOffset ObservedAtUtc { get; init; }
    /// <summary>Gets the search filter.</summary>
    public string? Search { get; init; }
    /// <summary>Gets the status filter.</summary>
    public string? Status { get; init; }
    /// <summary>Gets the one-based page.</summary>
    public int Page { get; init; } = 1;
    /// <summary>Gets whether matching definitions have another page.</summary>
    public bool HasNextJobPage { get; init; }
    /// <summary>Gets whether matching execution attempts have another page.</summary>
    public bool HasNextExecutionPage { get; init; }
    /// <summary>Gets whether either result has another page.</summary>
    public bool HasNextPage => HasNextJobPage || HasNextExecutionPage;
}
