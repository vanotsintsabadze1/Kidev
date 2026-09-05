using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kidev.Core.Data;

namespace Kidev.Core;

/// <summary>Provides bounded, read-only dashboard queries independently of the storage provider.</summary>
public interface IDashboardQuery
{
    /// <summary>Reads a safe, bounded factory snapshot and two minutes of overlapping persisted transitions.</summary>
    Task<FactorySnapshot> ReadFactoryAsync(CancellationToken cancellationToken);

    /// <summary>Reads global counts and a page of matching jobs and execution attempts.</summary>
    Task<DashboardSnapshot> ReadAsync(string? search, string? status, int page, CancellationToken cancellationToken);

    /// <summary>Reads a job and its most recent execution attempts, or null when absent.</summary>
    Task<DashboardJobDetails?> FindAsync(int id, CancellationToken cancellationToken);
}

/// <summary>Contains bounded dashboard data observed at a particular time.</summary>
public sealed record DashboardSnapshot
{
    /// <summary>Gets the total number of definitions.</summary>
    public long TotalJobs { get; }
    /// <summary>Gets the number of enabled, due jobs without a live lease.</summary>
    public long DueJobs { get; }
    /// <summary>Gets the number of running attempts with a live lease.</summary>
    public long Running { get; }
    /// <summary>Gets the number of successful attempts.</summary>
    public long Succeeded { get; }
    /// <summary>Gets the number of failed attempts.</summary>
    public long Failed { get; }
    /// <summary>Gets the number of expired attempts, including unfinalized expired leases.</summary>
    public long Expired { get; }
    /// <summary>Gets this page of matching definitions.</summary>
    public IReadOnlyList<JobDefinition> Jobs { get; }
    /// <summary>Gets this page of matching execution attempts.</summary>
    public IReadOnlyList<JobExecution> Executions { get; }
    /// <summary>Gets the observation timestamp.</summary>
    public DateTimeOffset ObservedAtUtc { get; }
    /// <summary>Gets whether matching definitions have another page.</summary>
    public bool HasNextJobPage { get; }
    /// <summary>Gets whether matching execution attempts have another page.</summary>
    public bool HasNextExecutionPage { get; }

    /// <summary>Creates a dashboard snapshot.</summary>
    public DashboardSnapshot(long totalJobs, long dueJobs, long running, long succeeded, long failed, long expired,
        IReadOnlyList<JobDefinition> jobs, IReadOnlyList<JobExecution> executions, DateTimeOffset observedAtUtc,
        bool hasNextJobPage, bool hasNextExecutionPage)
    {
        TotalJobs = totalJobs;
        DueJobs = dueJobs;
        Running = running;
        Succeeded = succeeded;
        Failed = failed;
        Expired = expired;
        Jobs = jobs;
        Executions = executions;
        ObservedAtUtc = observedAtUtc;
        HasNextJobPage = hasNextJobPage;
        HasNextExecutionPage = hasNextExecutionPage;
    }
}

/// <summary>Contains a definition and at most fifty recent attempts.</summary>
public sealed record DashboardJobDetails
{
    /// <summary>Gets the definition.</summary>
    public JobDefinition Job { get; }
    /// <summary>Gets recent execution attempts.</summary>
    public IReadOnlyList<JobExecution> Executions { get; }

    /// <summary>Creates job details.</summary>
    public DashboardJobDetails(JobDefinition job, IReadOnlyList<JobExecution> executions)
    {
        Job = job;
        Executions = executions;
    }
}
